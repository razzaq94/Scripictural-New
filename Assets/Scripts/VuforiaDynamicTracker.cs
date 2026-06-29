using Solo.MOST_IN_ONE;
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
        public float aspect = 1f;
        public ImageTargetBehaviour imageTarget;
        public VuforiaArtworkVideoSurface videoSurface;
        public bool isVideoReady;
        public bool isActivelyTracked;
    }

    [Header("Target")]
    [SerializeField] private float physicalWidthMeters = 0.2f;
    [SerializeField] private bool cacheMarkerImages = true;
    [SerializeField] private Material videoMaterial;

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingCanvasPrefab;

    [Header("URLs")]
    [SerializeField] private string assetBaseUrl = "https://api.scripictural.tecshield.net/";

    [Header("Scanner Control")]
    [SerializeField] private float scanUnlockDelay = 0.75f;
    [SerializeField] private float trackingHeartbeatTimeout = 1.0f;

    private readonly Dictionary<string, RuntimeArtworkData> runtimeArtworkMap = new();
    private readonly Dictionary<string, GameObject> spawnedLoadingCanvases = new();
    private readonly Dictionary<string, Texture2D> preloadedMarkerTextures = new();
    private readonly Dictionary<string, Coroutine> markerPreloadRoutines = new();
    private readonly HashSet<string> processingArtworkIds = new();
    private readonly HashSet<string> artworksInLoadingState = new();
    private readonly Dictionary<string, System.Action> preparedHandlers = new();

    private string activeVerifiedArtworkId;
    private bool isVerifiedArtworkCurrentlyTracking;
    private float lastTrackingSeenTime = -999f;
    private Coroutine unlockRoutine;
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


        ChatManager.instance.SetCurrentArtworkId(detectData.artworkId);
        ChatManager.instance.SetCurrentDescription(detectData.artwork.metaData.description);

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

        //if (texture.width > 2048 || texture.height > 2048)
        //{
        //    Debug.LogError(LogTag + $" Marker too large: {texture.width}x{texture.height}");
        //    FinishProcessing(artworkId);
        //    yield break;
        //}

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
        SetMarkerPreviewVisible(imageTarget, false);
        imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;

        string localVideoPath = ArtworkSessionCache.GetVideoPath(artworkId);
        bool hasLocalVideo = ArtworkSessionCache.HasVideo(artworkId);

        GameObject videoRoot = new GameObject("ArtworkVideo_" + artworkId);
        VuforiaArtworkVideoSurface videoSurface = videoRoot.AddComponent<VuforiaArtworkVideoSurface>();
        System.Action preparedHandler = () => OnVideoSurfacePrepared(artworkId);
        preparedHandlers[artworkId] = preparedHandler;
        videoSurface.PreparedForPlayback += preparedHandler;

        RuntimeArtworkData data = new RuntimeArtworkData
        {
            artworkId = artworkId,
            imageUrl = imageUrl,
            remoteVideoUrl = videoUrl,
            localVideoPath = localVideoPath,
            markerTexture = texture,
            aspect = (float)texture.width / texture.height,
            imageTarget = imageTarget,
            videoSurface = videoSurface,
            isVideoReady = hasLocalVideo
        };

        runtimeArtworkMap[artworkId] = data;
        EnsureLoadingCanvas(data);

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

        // Only show video while the physical image is visible — not EXTENDED_TRACKED.
        bool activelyTracked = status.Status == Status.TRACKED;
        bool wasActivelyTracked = data.isActivelyTracked;
        data.isActivelyTracked = activelyTracked;
        runtimeArtworkMap[artworkId] = data;

        if (activelyTracked)
        {
            if (!wasActivelyTracked)
                MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.LightImpact);

            MarkArtworkTracking(artworkId);
            OnArtworkTracked(data);
        }
        else
        {
            OnArtworkLost(data);
            MarkArtworkLost(artworkId);
        }
    }

    private void OnArtworkTracked(RuntimeArtworkData data)
    {
        EnsureLoadingCanvas(data);
        SetMarkerPreviewVisible(data.imageTarget, false);

        if (data.videoSurface != null)
            data.videoSurface.gameObject.SetActive(true);

        data.videoSurface?.HideQuad();

        bool waitingForVideo = artworksInLoadingState.Contains(data.artworkId) || !data.isVideoReady;
        if (waitingForVideo)
        {
            SetLoadingCanvasActive(data.artworkId, true);
            return;
        }

        SetLoadingCanvasActive(data.artworkId, true);
        data.videoSurface?.HandleTrackingChanged(true);
    }

    private void OnArtworkLost(RuntimeArtworkData data)
    {
        data.isActivelyTracked = false;
        runtimeArtworkMap[data.artworkId] = data;

        data.videoSurface?.HandleTrackingChanged(false);

        if (data.videoSurface != null)
            data.videoSurface.gameObject.SetActive(false);

        SetLoadingCanvasActive(data.artworkId, false);
        SetMarkerPreviewVisible(data.imageTarget, false);
    }

    private void OnVideoSurfacePrepared(string artworkId)
    {
        if (!runtimeArtworkMap.TryGetValue(artworkId, out RuntimeArtworkData data))
            return;

        data.isVideoReady = true;
        runtimeArtworkMap[artworkId] = data;

        if (!data.isActivelyTracked)
            return;

        SetArtworkLoadingState(artworkId, false);
        SetLoadingCanvasActive(artworkId, false);
        data.videoSurface?.ShowQuadAndPlay();
    }

    private void EnsureLoadingCanvas(RuntimeArtworkData data)
    {
        if (loadingCanvasPrefab == null || data.imageTarget == null)
            return;

        if (spawnedLoadingCanvases.ContainsKey(data.artworkId))
            return;

        GameObject loadingGo = Instantiate(loadingCanvasPrefab, data.imageTarget.transform);
        loadingGo.name = "LoadingCanvas_" + data.artworkId;
        UpdateLoadingCanvasTransform(loadingGo, data);
        loadingGo.SetActive(false);
        spawnedLoadingCanvases[data.artworkId] = loadingGo;
    }

    private void UpdateLoadingCanvasTransform(GameObject loadingGo, RuntimeArtworkData data)
    {
        if (loadingGo == null || data == null)
            return;

        RectTransform rect = loadingGo.GetComponent<RectTransform>();
        if (rect == null)
            return;

        Canvas canvas = loadingGo.GetComponent<Canvas>();
        float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
        if (ppu <= 0f)
            ppu = 100f;

        float targetWidth = physicalWidthMeters;
        float targetHeight = targetWidth / Mathf.Max(0.01f, data.aspect);

        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.Euler(90f, 0f, 0f);
        rect.sizeDelta = new Vector2(targetWidth * ppu, targetHeight * ppu);

        float worldScale = 1.011f / ppu;
        rect.localScale = new Vector3(worldScale, worldScale, worldScale);
        LayoutSquareLoadingIndicator(loadingGo.transform, targetWidth * ppu * 0.15f);
    }

    private static void LayoutSquareLoadingIndicator(Transform root, float squareSize)
    {
        if (root == null)
            return;

        Transform loading13 = root.Find("Loading13");
        if (loading13 == null)
            return;

        RectTransform loadingRect = loading13 as RectTransform;
        if (loadingRect == null)
            return;

        loadingRect.anchorMin = new Vector2(0.5f, 0.5f);
        loadingRect.anchorMax = new Vector2(0.5f, 0.5f);
        loadingRect.pivot = new Vector2(0.5f, 0.5f);
        loadingRect.anchoredPosition = Vector2.zero;
        loadingRect.sizeDelta = new Vector2(squareSize, squareSize);
        loadingRect.localScale = Vector3.one;
    }

    private void SetLoadingCanvasActive(string artworkId, bool active)
    {
        if (!spawnedLoadingCanvases.TryGetValue(artworkId, out GameObject loadingGo) || loadingGo == null)
            return;

        loadingGo.SetActive(active);
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

    private static void SetMarkerPreviewVisible(ImageTargetBehaviour target, bool visible)
    {
        if (target == null)
            return;

        Renderer previewRenderer = target.GetComponentInChildren<Renderer>();
        if (previewRenderer != null)
            previewRenderer.enabled = visible;
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
        foreach (RuntimeArtworkData data in runtimeArtworkMap.Values)
        {
            if (data.isActivelyTracked)
                return true;
        }

        return false;
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

        if (!runtimeArtworkMap.TryGetValue(artworkId, out RuntimeArtworkData data))
            return;

        if (!data.isActivelyTracked)
            return;

        if (isLoading)
        {
            data.videoSurface?.HideQuad();
            SetLoadingCanvasActive(artworkId, true);
            return;
        }

        if (data.videoSurface != null && data.videoSurface.IsVideoPrepared)
        {
            SetLoadingCanvasActive(artworkId, false);
            data.videoSurface.ShowQuadAndPlay();
        }
        else
        {
            SetLoadingCanvasActive(artworkId, true);
            data.videoSurface?.HandleTrackingChanged(true);
        }
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

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            HideAllArtworkPresentation();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            HideAllArtworkPresentation();
    }

    private void HideAllArtworkPresentation()
    {
        foreach (RuntimeArtworkData data in runtimeArtworkMap.Values)
            OnArtworkLost(data);
    }

    private void OnDestroy()
    {
        foreach (RuntimeArtworkData data in runtimeArtworkMap.Values)
        {
            if (data.imageTarget != null)
                data.imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;

            if (data.videoSurface != null &&
                preparedHandlers.TryGetValue(data.artworkId, out System.Action handler))
            {
                data.videoSurface.PreparedForPlayback -= handler;
            }
        }
    }
}
