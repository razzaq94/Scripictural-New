using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

public class ArtworkVideoSurface : MonoBehaviour
{
    private const int MaxPrepareRetries = 4;
    private const float PrepareRetryDelaySeconds = 0.5f;
    private const int LowEndMemoryMb = 3072;
    private const int LowEndMaxVideoWidth = 720;

    [SerializeField] private Material videoMaterial;
    [SerializeField] private string videoTextureProperty = "_MainTex";
    [SerializeField] private float surfaceYOffset = 0.002f;
    [SerializeField] private bool loop = true;

    private GameObject surfaceRoot;
    private MeshRenderer videoMeshRenderer;
    private Material videoMaterialInstance;
    private VideoPlayer videoPlayer;
    private RenderTexture videoRenderTexture;
    private float markerAspect = 1f;

    private string remoteVideoUrl;
    private string localVideoPath;
    private bool preferLocalPlayback;
    private bool remotePlaybackFailed;
    private bool isPreparing;
    private int localUrlVariantIndex;
    private Coroutine prepareRoutine;

    public bool IsVideoPrepared { get; private set; }
    public bool IsTracked { get; private set; }
    public bool IsPlayingFromLocal { get; private set; }
    public bool RemotePlaybackFailed => remotePlaybackFailed;
    public event Action PreparedForPlayback;

    public static bool ShouldPreferLocalPlayback()
    {
        if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize <= LowEndMemoryMb)
            return true;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize <= 512)
            return true;
#endif
        return false;
    }

    public Task SetupAsync(
        Transform parent,
        Texture2D targetTexture,
        float targetWidthMeters,
        string remoteVideoUrl,
        string localVideoPath = null,
        Material materialOverride = null,
        bool? preferLocalPlaybackOverride = null)
    {
        transform.SetParent(parent, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (materialOverride != null)
            videoMaterial = materialOverride;

        if (videoMaterial == null)
        {
            Debug.LogError("[ArtworkVideoSurface] Video material is not assigned.");
            return Task.CompletedTask;
        }

        this.remoteVideoUrl = remoteVideoUrl;
        this.localVideoPath = localVideoPath;
        preferLocalPlayback = preferLocalPlaybackOverride ?? ShouldPreferLocalPlayback();
        remotePlaybackFailed = false;
        markerAspect = targetTexture != null && targetTexture.width > 0
            ? (float)targetTexture.height / targetTexture.width
            : 1f;

        CreateMeshSurface(targetTexture, targetWidthMeters);
        HideQuad();
        AssignBestAvailableUrl();
        return Task.CompletedTask;
    }

    public void HandleTrackingChanged(bool tracked)
    {
        IsTracked = tracked;

        if (!tracked)
        {
            IsVideoPrepared = false;
            isPreparing = false;
            StopPrepareRoutine();
            HideQuad();
            PauseVideo();
            return;
        }

        if (string.IsNullOrEmpty(videoPlayer?.url) &&
            (string.IsNullOrWhiteSpace(localVideoPath) || !File.Exists(localVideoPath)) &&
            (preferLocalPlayback || remotePlaybackFailed || string.IsNullOrWhiteSpace(remoteVideoUrl)))
        {
            Debug.LogWarning("[ArtworkVideoSurface] Track started but video URL is not set yet.");
            return;
        }

        HideQuadVisual();
        BeginPlaybackPrepare();
    }

    public void ForceHide()
    {
        IsTracked = false;
        HideQuad();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            ForceHide();
    }

    public void ShowQuadAndPlay()
    {
        if (!IsTracked)
            return;

        if (surfaceRoot != null)
            surfaceRoot.SetActive(true);

        if (videoMeshRenderer != null)
            videoMeshRenderer.enabled = true;

        if (videoPlayer != null && videoPlayer.isPrepared)
            videoPlayer.Play();
    }

    public void HideQuad()
    {
        PauseVideo();
        HideQuadVisual();
    }

    private void HideQuadVisual()
    {
        if (videoMeshRenderer != null)
            videoMeshRenderer.enabled = false;

        if (surfaceRoot != null)
            surfaceRoot.SetActive(false);
    }

    public Task BindLocalVideoAsync(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !ArtworkSessionCache.IsValidVideoFile(localPath))
        {
            Debug.LogWarning("[ArtworkVideoSurface] Local video missing or invalid: " + localPath);
            return Task.CompletedTask;
        }

        localVideoPath = localPath;
        localUrlVariantIndex = 0;
        IsVideoPrepared = false;
        AssignBestAvailableUrl(forceLocal: true);

        if (IsTracked)
            BeginPlaybackPrepare();

        return Task.CompletedTask;
    }

    private void AssignBestAvailableUrl(bool forceLocal = false)
    {
        if (videoPlayer == null)
            return;

        string playUrl = null;
        bool fromLocal = false;

        bool localReady = ArtworkSessionCache.IsValidVideoFile(localVideoPath);
        bool blockRemote = forceLocal || preferLocalPlayback || remotePlaybackFailed;

        if (localReady)
        {
            playUrl = GetLocalPlayUrl(localVideoPath, localUrlVariantIndex);
            fromLocal = true;
        }
        else if (!blockRemote && !string.IsNullOrWhiteSpace(remoteVideoUrl))
        {
            playUrl = remoteVideoUrl;
            fromLocal = false;
        }

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            Debug.LogWarning("[ArtworkVideoSurface] No video URL/path available yet.");
            return;
        }

        IsPlayingFromLocal = fromLocal;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = playUrl;
        Debug.Log("[ArtworkVideoSurface] Video URL set (" + (fromLocal ? "local" : "remote") + "): " + playUrl);
    }

    private void BeginPlaybackPrepare()
    {
        if (videoPlayer == null)
            return;

        if (string.IsNullOrEmpty(videoPlayer.url))
            AssignBestAvailableUrl();

        if (string.IsNullOrEmpty(videoPlayer.url))
            return;

        StopPrepareRoutine();
        prepareRoutine = StartCoroutine(PrepareWithRetries());
    }

    private IEnumerator PrepareWithRetries()
    {
        isPreparing = true;
        IsVideoPrepared = false;
        HideQuadVisual();

        // Let filesystem settle after DownloadHandlerFile rename on Android.
        if (IsPlayingFromLocal)
        {
            yield return null;
            yield return null;
        }

        EnsureVideoPlayerReady();

        for (int attempt = 1; attempt <= MaxPrepareRetries; attempt++)
        {
            if (!IsTracked || videoPlayer == null)
                break;

            if (IsPlayingFromLocal && ArtworkSessionCache.IsValidVideoFile(localVideoPath))
            {
                string playUrl = GetLocalPlayUrl(localVideoPath, localUrlVariantIndex);
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = playUrl;
                Debug.Log("[ArtworkVideoSurface] Prepare local variant " + localUrlVariantIndex + ": " + playUrl);
            }
            else if (string.IsNullOrEmpty(videoPlayer.url))
            {
                AssignBestAvailableUrl();
            }

            if (string.IsNullOrEmpty(videoPlayer.url))
                break;

            bool prepared = false;
            bool failed = false;
            string failMessage = null;

            void OnPrepared(VideoPlayer _)
            {
                prepared = true;
            }

            void OnFailed(VideoPlayer _, string message)
            {
                failed = true;
                failMessage = message;
            }

            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.errorReceived += OnFailed;

            try
            {
                if (videoPlayer.isPlaying)
                    videoPlayer.Stop();
                videoPlayer.Prepare();
            }
            catch (Exception ex)
            {
                failed = true;
                failMessage = ex.Message;
            }

            float timeout = IsPlayingFromLocal ? 45f : 45f;
            float elapsed = 0f;
            while (!prepared && !failed && elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.errorReceived -= OnFailed;
            videoPlayer.errorReceived += OnVideoError;

            if (prepared)
            {
                OnVideoPrepared(videoPlayer);
                isPreparing = false;
                prepareRoutine = null;
                yield break;
            }

            Debug.LogWarning(
                "[ArtworkVideoSurface] Prepare attempt " + attempt + "/" + MaxPrepareRetries +
                " failed: " + (failMessage ?? (elapsed >= timeout ? "timeout" : "unknown")));

            if (!IsPlayingFromLocal && ArtworkSessionCache.IsValidVideoFile(localVideoPath))
            {
                Debug.LogWarning("[ArtworkVideoSurface] Falling back to local video after remote prepare failure.");
                remotePlaybackFailed = true;
                localUrlVariantIndex = 0;
                IsPlayingFromLocal = true;
                AssignBestAvailableUrl(forceLocal: true);
            }
            else if (!IsPlayingFromLocal)
            {
                remotePlaybackFailed = true;
            }
            else
            {
                // Cycle Android path formats / cache copy, then recreate player.
                localUrlVariantIndex++;
                if (localUrlVariantIndex >= GetLocalUrlVariantCount())
                {
                    localUrlVariantIndex = 0;
                    RecreateVideoPlayer();
                }
            }

            if (attempt < MaxPrepareRetries)
                yield return new WaitForSecondsRealtime(PrepareRetryDelaySeconds * attempt);
        }

        isPreparing = false;
        prepareRoutine = null;
        IsVideoPrepared = false;
        Debug.LogError("[ArtworkVideoSurface] Video prepare failed after retries.");
    }

    private static int GetLocalUrlVariantCount() => 4;

    private static string GetLocalPlayUrl(string path, int variantIndex)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string absolute = path;
        try { absolute = Path.GetFullPath(path); }
        catch { /* keep original */ }

        absolute = absolute.Replace('\\', '/');

        switch (variantIndex % GetLocalUrlVariantCount())
        {
            case 0:
                // Android MediaPlayer usually wants a raw filesystem path (no file://).
                return absolute;
            case 1:
                return "file://" + absolute;
            case 2:
                try { return new Uri(absolute).AbsoluteUri; }
                catch { return absolute; }
            default:
                return CopyToPlaybackCache(absolute) ?? absolute;
        }
    }

    private static string CopyToPlaybackCache(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return null;

            string dir = Path.Combine(Application.temporaryCachePath, "video_playback");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string dest = Path.Combine(dir, "current.mp4");
            if (File.Exists(dest))
                File.Delete(dest);

            File.Copy(sourcePath, dest, true);
            return dest.Replace('\\', '/');
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ArtworkVideoSurface] Playback cache copy failed: " + ex.Message);
            return null;
        }
    }

    private void CreateMeshSurface(Texture2D targetTexture, float targetWidthMeters)
    {
        surfaceRoot = new GameObject("VideoSurface");
        surfaceRoot.transform.SetParent(transform, false);
        surfaceRoot.transform.localPosition = new Vector3(0f, surfaceYOffset, 0f);
        surfaceRoot.transform.localRotation = Quaternion.identity;
        surfaceRoot.SetActive(false);

        float aspect = (float)targetTexture.height / targetTexture.width;
        markerAspect = aspect;
        float width = targetWidthMeters;
        float height = targetWidthMeters * aspect;

        MeshFilter meshFilter = surfaceRoot.AddComponent<MeshFilter>();
        videoMeshRenderer = surfaceRoot.AddComponent<MeshRenderer>();
        videoMeshRenderer.enabled = false;
        meshFilter.mesh = CreateTargetSizedMesh(width, height);

        videoMaterialInstance = new Material(videoMaterial);
        videoMeshRenderer.material = videoMaterialInstance;

        CreateVideoPlayerObject();
        ConfigureVideoPlayerForDevice();
    }

    private void CreateVideoPlayerObject()
    {
        GameObject videoObj = new GameObject("VideoPlayer");
        videoObj.transform.SetParent(transform, false);
        videoPlayer = videoObj.AddComponent<VideoPlayer>();
    }

    private void EnsureVideoPlayerReady()
    {
        if (videoPlayer == null)
            CreateVideoPlayerObject();

        ConfigureVideoPlayerForDevice();
    }

    private void ConfigureVideoPlayerForDevice()
    {
        if (videoPlayer == null || videoMeshRenderer == null)
            return;

        videoPlayer.source = VideoSource.Url;
        videoPlayer.isLooping = loop;
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

        if (preferLocalPlayback)
        {
            // Lower decode memory pressure on weak devices.
            int w = LowEndMaxVideoWidth;
            int h = Mathf.Max(2, Mathf.RoundToInt(w * markerAspect));
            if (h % 2 != 0) h++;

            if (videoRenderTexture == null ||
                videoRenderTexture.width != w ||
                videoRenderTexture.height != h)
            {
                if (videoRenderTexture != null)
                {
                    videoRenderTexture.Release();
                    Destroy(videoRenderTexture);
                }

                videoRenderTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "ArtworkVideoRT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                videoRenderTexture.Create();
            }

            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoRenderTexture;
            if (videoMaterialInstance != null)
                videoMaterialInstance.SetTexture(videoTextureProperty, videoRenderTexture);
        }
        else
        {
            videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
            videoPlayer.targetMaterialRenderer = videoMeshRenderer;
            videoPlayer.targetMaterialProperty = videoTextureProperty;
        }
    }

    private void RecreateVideoPlayer()
    {
        Debug.LogWarning("[ArtworkVideoSurface] Recreating VideoPlayer after prepare failure.");

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.Stop();
            Destroy(videoPlayer.gameObject);
            videoPlayer = null;
        }

        CreateVideoPlayerObject();
        ConfigureVideoPlayerForDevice();
        AssignBestAvailableUrl(forceLocal: IsPlayingFromLocal || preferLocalPlayback || remotePlaybackFailed);
    }

    private static Mesh CreateTargetSizedMesh(float width, float height)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        Mesh mesh = new Mesh { name = "ArtworkVideoMesh" };
        mesh.vertices = new[]
        {
            new Vector3(-halfW, 0f, -halfH),
            new Vector3( halfW, 0f, -halfH),
            new Vector3(-halfW, 0f,  halfH),
            new Vector3( halfW, 0f,  halfH)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnVideoPrepared;
        IsVideoPrepared = true;
        remotePlaybackFailed = false;
        Debug.Log("[ArtworkVideoSurface] Video prepared (" + (IsPlayingFromLocal ? "local" : "remote") + ").");
        PreparedForPlayback?.Invoke();

        if (IsTracked)
            ShowQuadAndPlay();
    }

    private void PauseVideo()
    {
        if (videoPlayer != null)
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Pause();
            videoPlayer.Stop();
        }
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError("[ArtworkVideoSurface] Video error: " + message);
        IsVideoPrepared = false;

        if (!IsPlayingFromLocal)
            remotePlaybackFailed = true;
    }

    private void StopPrepareRoutine()
    {
        if (prepareRoutine != null)
        {
            StopCoroutine(prepareRoutine);
            prepareRoutine = null;
        }

        isPreparing = false;
    }

    private void OnDestroy()
    {
        StopPrepareRoutine();

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }

        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            Destroy(videoRenderTexture);
            videoRenderTexture = null;
        }
    }
}
