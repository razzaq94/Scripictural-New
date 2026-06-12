using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

public class VuforiaArtworkVideoSurface : MonoBehaviour
{
    [SerializeField] private Material videoMaterial;
    [SerializeField] private string videoTextureProperty = "_MainTex";
    [SerializeField] private float surfaceYOffset = 0.002f;
    [SerializeField] private bool loop = true;

    private GameObject surfaceRoot;
    private MeshRenderer videoMeshRenderer;
    private VideoPlayer videoPlayer;

    public bool IsVideoPrepared { get; private set; }
    public bool IsTracked { get; private set; }
    public event Action PreparedForPlayback;

    public Task SetupAsync(
        Transform parent,
        Texture2D targetTexture,
        float targetWidthMeters,
        string remoteVideoUrl,
        string localVideoPath = null,
        Material materialOverride = null)
    {
        transform.SetParent(parent, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (materialOverride != null)
            videoMaterial = materialOverride;

        if (videoMaterial == null)
        {
            Debug.LogError("[VuforiaArtworkVideoSurface] Video material is not assigned.");
            return Task.CompletedTask;
        }

        CreateMeshSurface(targetTexture, targetWidthMeters);
        HideQuad();
        AssignVideoUrl(localVideoPath, remoteVideoUrl);
        return Task.CompletedTask;
    }

    public void HandleTrackingChanged(bool tracked)
    {
        IsTracked = tracked;

        if (!tracked)
        {
            HideQuad();
            PauseVideo();
            return;
        }

        if (string.IsNullOrEmpty(videoPlayer?.url))
        {
            Debug.LogWarning("[VuforiaArtworkVideoSurface] Track started but video URL is not set yet.");
            return;
        }

        HideQuadVisual();
        BeginPlaybackPrepare();
    }

    public void ShowQuadAndPlay()
    {
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

    public Task BindLocalVideoAsync(string localVideoPath)
    {
        if (string.IsNullOrWhiteSpace(localVideoPath) || !File.Exists(localVideoPath))
            return Task.CompletedTask;

        IsVideoPrepared = false;
        AssignVideoUrl(localVideoPath, null);

        if (IsTracked)
            BeginPlaybackPrepare();

        return Task.CompletedTask;
    }

    private void AssignVideoUrl(string localVideoPath, string remoteVideoUrl)
    {
        if (videoPlayer == null)
            return;

        string playUrl = null;

        if (!string.IsNullOrWhiteSpace(localVideoPath) && File.Exists(localVideoPath))
            playUrl = ToVideoPlayerUrl(localVideoPath);
        else if (!string.IsNullOrWhiteSpace(remoteVideoUrl))
            playUrl = remoteVideoUrl;

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            Debug.LogWarning("[VuforiaArtworkVideoSurface] No video URL/path available yet.");
            return;
        }

        videoPlayer.url = playUrl;
        Debug.Log("[VuforiaArtworkVideoSurface] Video URL set: " + playUrl);
    }

    private void BeginPlaybackPrepare()
    {
        if (videoPlayer == null || string.IsNullOrEmpty(videoPlayer.url))
            return;

        IsVideoPrepared = false;
        HideQuadVisual();

        if (!videoPlayer.gameObject.activeInHierarchy)
            videoPlayer.gameObject.SetActive(true);

        videoPlayer.enabled = true;
        videoPlayer.prepareCompleted -= OnVideoPrepared;
        videoPlayer.errorReceived -= OnVideoError;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.Stop();
        videoPlayer.Prepare();
        videoPlayer.prepareCompleted += OnVideoPrepared;
    }

    private static string ToVideoPlayerUrl(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return null;

        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return pathOrUrl;

        try
        {
            return new Uri(Path.GetFullPath(pathOrUrl)).AbsoluteUri;
        }
        catch (Exception ex)
        {
            Debug.LogError("[VuforiaArtworkVideoSurface] Invalid local video path: " + ex.Message);
            return pathOrUrl;
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
        float width = targetWidthMeters;
        float height = targetWidthMeters * aspect;

        MeshFilter meshFilter = surfaceRoot.AddComponent<MeshFilter>();
        videoMeshRenderer = surfaceRoot.AddComponent<MeshRenderer>();
        videoMeshRenderer.enabled = false;
        meshFilter.mesh = CreateTargetSizedMesh(width, height);

        Material materialInstance = new Material(videoMaterial);
        videoMeshRenderer.material = materialInstance;

        GameObject videoObj = new GameObject("VideoPlayer");
        videoObj.transform.SetParent(transform, false);

        videoPlayer = videoObj.AddComponent<VideoPlayer>();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = videoMeshRenderer;
        videoPlayer.targetMaterialProperty = videoTextureProperty;
        videoPlayer.isLooping = loop;
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
    }

    private static Mesh CreateTargetSizedMesh(float width, float height)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        Mesh mesh = new Mesh { name = "VuforiaArtworkVideoMesh" };
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
        Debug.Log("[VuforiaArtworkVideoSurface] Video prepared.");
        PreparedForPlayback?.Invoke();

        if (IsTracked)
            ShowQuadAndPlay();
    }

    private void PauseVideo()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Pause();
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError("[VuforiaArtworkVideoSurface] Video error: " + message);
        IsVideoPrepared = false;
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }
    }
}
