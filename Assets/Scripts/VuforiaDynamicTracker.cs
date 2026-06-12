using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Vuforia;

public class VuforiaDynamicTracker : MonoBehaviour
{
    private const string LogTag = "[VuforiaDynamicTracker]";

    [System.Serializable]
    public class RuntimeArtworkData
    {
        public string artworkId;
        public string imageUrl;
        public string remoteVideoUrl;
        public string localVideoPath;
        public Texture2D markerTexture;
        public ImageTargetBehaviour imageTarget;
        public VuforiaArtworkVideoSurface videoSurface;
        public bool isVideoReady;
    }

    [Header("Target")]
    [SerializeField] private float physicalWidthMeters = 0.2f;
    [SerializeField] private bool cacheMarkerImages = true;
    [SerializeField] private Material videoMaterial;

    [Header("URLs")]
    [SerializeField] private string assetBaseUrl = "https://api.scripictural.tecshield.net/";

    [Header("Scanner Control")]
    [SerializeField] private float scanUnlockDelay = 0.75f;
    [SerializeField] private float trackingHeartbeatTimeout = 1.0f;

    private readonly Dictionary<string, RuntimeArtworkData> runtimeArtworkMap = new();
    private readonly Dictionary<string, Texture2D> preloadedMarkerTextures = new();
    private readonly Dictionary<string, Coroutine> markerPreloadRoutines = new();
    private readonly HashSet<string> processingArtworkIds = new();
    private readonly HashSet<string> artworksInLoadingState = new();

    private string activeVerifiedArtworkId;
    private bool isVerifiedArtworkCurrentlyTracking;
    private float lastTrackingSeenTime = -999f;
    private Coroutine unlockRoutine;
    private bool isScannerBlockedByLoadState;

    public void OnArtworkDetected(ServerManager.MatchResponse detectData)
    {
        if (detectData == null)
        {
            Debug.LogError(LogTag + " Detect data is null.");
            return;
        }

        string imageUrl = detectData.artwork != null ? detectData.artwork.imageURL : string.Empty;
        string videoUrl = string.Empty;

        if (detectData.artwork != null)
        {
            videoUrl = !string.IsNullOrWhiteSpace(detectData.artwork.compressedVideoUrl)
                ? detectData.artwork.compressedVideoUrl
                : !string.IsNullOrWhiteSpace(detectData.artwork.videoURL)
                    ? detectData.artwork.videoURL
                    : detectData.artwork.originalVideoUrl;
        }

        OnArtworkDetected(detectData.artworkId, imageUrl, videoUrl);
    }

    public void OnArtworkDetected(string artworkId, string imageUrl, string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkId))
            return;

        activeVerifiedArtworkId = artworkId;

        if (runtimeArtworkMap.ContainsKey(artworkId))
        {
            Debug.Log(LogTag + " Artwork already added: " + artworkId);
            return;
        }

        if (processingArtworkIds.Contains(artworkId))
        {
            Debug.Log(LogTag + " Already processing: " + artworkId);
            return;
        }

        SetArtworkLoadingState(artworkId, true);
        string resolvedImageUrl = ResolveUrl(imageUrl);
        string resolvedVideoUrl = ResolveUrl(videoUrl);

        ArtworkSessionCache.UpsertArtworkRecord(artworkId, resolvedImageUrl, resolvedVideoUrl);
        StartMarkerPreload(artworkId, resolvedImageUrl);
        StartCoroutine(SetupVuforiaTarget(artworkId, resolvedImageUrl, resolvedVideoUrl));
    }

    private IEnumerator SetupVuforiaTarget(string artworkId, string imageUrl, string videoUrl)
    {
        processingArtworkIds.Add(artworkId);
        SetArtworkLoadingState(artworkId, true);

        if (VuforiaBehaviour.Instance == null)
        {
            Debug.LogError(LogTag + " VuforiaBehaviour is not available.");
            FinishProcessing(artworkId);
            yield break;
        }

        Texture2D texture = null;
        bool loadedFromCache = false;

        if (cacheMarkerImages && ArtworkSessionCache.HasImage(artworkId))
        {
            texture = ArtworkSessionCache.LoadImage(artworkId);
            loadedFromCache = texture != null;
        }

        if (texture == null)
        {
            yield return WaitForMarkerTexture(artworkId);
            preloadedMarkerTextures.TryGetValue(artworkId, out texture);
            preloadedMarkerTextures.Remove(artworkId);
        }

        if (texture == null)
        {
            Debug.LogError(LogTag + " Failed to load marker texture for " + artworkId);
            FinishProcessing(artworkId);
            yield break;
        }

        if (texture.width > 2048 || texture.height > 2048)
        {
            Debug.LogError(LogTag + $" Marker too large: {texture.width}x{texture.height}");
            FinishProcessing(artworkId);
            yield break;
        }

        Task<ImageTargetBehaviour> createTask = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTargetAsync(
            texture,
            physicalWidthMeters,
            artworkId
        );

        while (!createTask.IsCompleted)
            yield return null;

        ImageTargetBehaviour imageTarget = createTask.Result;
        if (imageTarget == null)
        {
            Debug.LogError(LogTag + " Vuforia failed to create image target for " + artworkId);
            FinishProcessing(artworkId);
            yield break;
        }

        imageTarget.name = artworkId;
        imageTarget.transform.SetParent(transform, false);
        AssignMarkerPreview(imageTarget, texture);
        imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;

        string localVideoPath = ArtworkSessionCache.GetVideoPath(artworkId);
        bool hasLocalVideo = ArtworkSessionCache.HasVideo(artworkId);

        GameObject videoRoot = new GameObject("ArtworkVideo_" + artworkId);
        VuforiaArtworkVideoSurface videoSurface = videoRoot.AddComponent<VuforiaArtworkVideoSurface>();

        RuntimeArtworkData data = new RuntimeArtworkData
        {
            artworkId = artworkId,
            imageUrl = imageUrl,
            remoteVideoUrl = videoUrl,
            localVideoPath = localVideoPath,
            markerTexture = texture,
            imageTarget = imageTarget,
            videoSurface = videoSurface,
            isVideoReady = hasLocalVideo
        };

        runtimeArtworkMap[artworkId] = data;

        Task setupVideoTask = videoSurface.SetupAsync(
            imageTarget.transform,
            texture,
            physicalWidthMeters,
            videoUrl,
            hasLocalVideo ? localVideoPath : null,
            videoMaterial
        );

        while (!setupVideoTask.IsCompleted)
            yield return null;

        data.isVideoReady = videoSurface.IsReady;

        if (cacheMarkerImages && !loadedFromCache)
            StartCoroutine(CacheMarkerImageDeferred(texture, artworkId));

        if (!hasLocalVideo && !string.IsNullOrWhiteSpace(videoUrl))
            StartCoroutine(DownloadVideoAndBind(data));
        else
            SetArtworkLoadingState(artworkId, false);

        processingArtworkIds.Remove(artworkId);
        Debug.Log(LogTag + " Runtime target ready | artworkId=" + artworkId);
    }

    private IEnumerator DownloadVideoAndBind(RuntimeArtworkData data)
    {
        yield return ArtworkSessionCache.DownloadAndSave(data.remoteVideoUrl, data.localVideoPath);

        if (!File.Exists(data.localVideoPath))
        {
            Debug.LogWarning(LogTag + " Video download failed | artworkId=" + data.artworkId);
            SetArtworkLoadingState(data.artworkId, false);
            yield break;
        }

        data.isVideoReady = true;
        runtimeArtworkMap[data.artworkId] = data;

        if (data.videoSurface != null)
        {
            Task bindTask = data.videoSurface.BindLocalVideoAsync(data.localVideoPath);
            while (!bindTask.IsCompleted)
                yield return null;
        }

        SetArtworkLoadingState(data.artworkId, false);
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        string artworkId = behaviour.name;
        if (!runtimeArtworkMap.TryGetValue(artworkId, out RuntimeArtworkData data))
            return;

        bool tracked = status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED;

        if (data.videoSurface != null)
            data.videoSurface.SetTracked(tracked);

        if (tracked)
            MarkArtworkTracking(artworkId);
        else
            MarkArtworkLost(artworkId);
    }

    private void StartMarkerPreload(string artworkId, string resolvedImageUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkId) || string.IsNullOrWhiteSpace(resolvedImageUrl))
            return;

        if (preloadedMarkerTextures.ContainsKey(artworkId) || markerPreloadRoutines.ContainsKey(artworkId))
            return;

        markerPreloadRoutines[artworkId] = StartCoroutine(PreloadMarkerTexture(artworkId, resolvedImageUrl));
    }

    private IEnumerator PreloadMarkerTexture(string artworkId, string resolvedImageUrl)
    {
        using UnityWebRequest req = UnityWebRequestTexture.GetTexture(resolvedImageUrl, false);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(LogTag + " Image download failed for " + artworkId + " | " + req.error);
            markerPreloadRoutines.Remove(artworkId);
            yield break;
        }

        Texture2D downloaded = DownloadHandlerTexture.GetContent(req);
        if (downloaded != null)
            preloadedMarkerTextures[artworkId] = downloaded;

        markerPreloadRoutines.Remove(artworkId);
    }

    private IEnumerator WaitForMarkerTexture(string artworkId)
    {
        if (preloadedMarkerTextures.ContainsKey(artworkId))
            yield break;

        if (!markerPreloadRoutines.ContainsKey(artworkId))
            yield break;

        yield return new WaitUntil(() =>
            preloadedMarkerTextures.ContainsKey(artworkId) ||
            !markerPreloadRoutines.ContainsKey(artworkId));
    }

    private IEnumerator CacheMarkerImageDeferred(Texture2D texture, string artworkId)
    {
        yield return null;
        ArtworkSessionCache.SaveImage(texture, artworkId);
    }

    private static void AssignMarkerPreview(ImageTargetBehaviour target, Texture2D texture)
    {
        Renderer previewRenderer = target.GetComponentInChildren<Renderer>();
        if (previewRenderer != null)
            previewRenderer.material.mainTexture = texture;
    }

    private string ResolveUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.StartsWith("http://") || value.StartsWith("https://"))
            return value;

        if (string.IsNullOrWhiteSpace(assetBaseUrl))
            return value;

        return assetBaseUrl.TrimEnd('/') + "/" + value.TrimStart('/');
    }

    public bool ShouldBlockScanner()
    {
        if (!string.IsNullOrEmpty(activeVerifiedArtworkId))
        {
            if (processingArtworkIds.Contains(activeVerifiedArtworkId) ||
                artworksInLoadingState.Contains(activeVerifiedArtworkId))
            {
                if (!isScannerBlockedByLoadState)
                {
                    isScannerBlockedByLoadState = true;
                    Debug.Log(LogTag + " Scanner blocked | artworkId=" + activeVerifiedArtworkId);
                }

                return true;
            }
        }
        else if (isScannerBlockedByLoadState)
        {
            isScannerBlockedByLoadState = false;
        }

        if (isScannerBlockedByLoadState)
            isScannerBlockedByLoadState = false;

        if (string.IsNullOrEmpty(activeVerifiedArtworkId) || !isVerifiedArtworkCurrentlyTracking)
            return false;

        if ((Time.time - lastTrackingSeenTime) > trackingHeartbeatTimeout)
        {
            isVerifiedArtworkCurrentlyTracking = false;
            return false;
        }

        return true;
    }

    public string GetActiveVerifiedArtworkId() => activeVerifiedArtworkId;

    private void SetArtworkLoadingState(string artworkId, bool isLoading)
    {
        if (string.IsNullOrWhiteSpace(artworkId))
            return;

        if (isLoading)
            artworksInLoadingState.Add(artworkId);
        else
            artworksInLoadingState.Remove(artworkId);
    }

    private void FinishProcessing(string artworkId)
    {
        processingArtworkIds.Remove(artworkId);
        SetArtworkLoadingState(artworkId, false);
    }

    private void MarkArtworkTracking(string artworkId)
    {
        activeVerifiedArtworkId = artworkId;
        isVerifiedArtworkCurrentlyTracking = true;
        lastTrackingSeenTime = Time.time;

        if (unlockRoutine != null)
        {
            StopCoroutine(unlockRoutine);
            unlockRoutine = null;
        }
    }

    private void MarkArtworkLost(string artworkId)
    {
        if (activeVerifiedArtworkId != artworkId)
            return;

        if (unlockRoutine != null)
            StopCoroutine(unlockRoutine);

        unlockRoutine = StartCoroutine(DelayedUnlock(artworkId));
    }

    private IEnumerator DelayedUnlock(string artworkId)
    {
        yield return new WaitForSeconds(scanUnlockDelay);

        if (activeVerifiedArtworkId == artworkId)
        {
            isVerifiedArtworkCurrentlyTracking = false;
            lastTrackingSeenTime = -999f;
        }

        unlockRoutine = null;
    }

    private void OnDestroy()
    {
        foreach (RuntimeArtworkData data in runtimeArtworkMap.Values)
        {
            if (data.imageTarget != null)
                data.imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;
        }
    }
}
