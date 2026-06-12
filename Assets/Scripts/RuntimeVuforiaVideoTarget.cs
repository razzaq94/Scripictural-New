using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using Vuforia;

public class RuntimeVuforiaVideoTarget : MonoBehaviour
{
    [Header("URLs")]
    [SerializeField] private string imageUrl;
    [SerializeField] private string videoUrl;

    [Header("Target")]
    [SerializeField] private string targetName = "RuntimeImageTarget";
    [SerializeField] private float targetWidthMeters = 0.2f;

    [Header("Video Surface")]
    [SerializeField] private Material videoMaterial;
    [SerializeField] private string videoTextureProperty = "_MainTex";
    [SerializeField] private float surfaceYOffset = 0.002f;

    [Header("Playback")]
    [SerializeField] private bool playOnTargetFound = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool downloadVideoBeforePlay = true;

    private ImageTargetBehaviour imageTarget;
    private VideoPlayer videoPlayer;
    private MeshRenderer videoMeshRenderer;

    private async void Start()
    {
        await BuildRuntimeTarget();
    }

    public async Task BuildRuntimeTarget()
    {
        Debug.Log("[RuntimeTarget] Starting runtime target creation...");

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            Debug.LogError("[RuntimeTarget] Image URL is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            Debug.LogError("[RuntimeTarget] Video URL is empty.");
            return;
        }

        if (videoMaterial == null)
        {
            Debug.LogError("[RuntimeTarget] Video material is not assigned.");
            return;
        }

        Texture2D targetTexture = await DownloadTexture(imageUrl);

        if (targetTexture == null)
        {
            Debug.LogError("[RuntimeTarget] Failed to download target image.");
            return;
        }

        if (targetTexture.width > 2048 || targetTexture.height > 2048)
        {
            Debug.LogError($"[RuntimeTarget] Target image too large: {targetTexture.width}x{targetTexture.height}. Max 2048x2048.");
            return;
        }

        Debug.Log($"[RuntimeTarget] Image downloaded: {targetTexture.width}x{targetTexture.height}");

        imageTarget = await VuforiaBehaviour.Instance.ObserverFactory.CreateImageTargetAsync(
            targetTexture,
            targetWidthMeters,
            targetName
        );

        if (imageTarget == null)
        {
            Debug.LogError("[RuntimeTarget] Vuforia failed to create Image Target.");
            return;
        }

        Debug.Log("[RuntimeTarget] Image Target created.");

        imageTarget.name = targetName;
        imageTarget.transform.SetParent(transform, false);

        AssignDownloadedTextureToImageTarget(imageTarget, targetTexture);

        await CreateVideoSurface(imageTarget.transform, targetTexture);

        imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;

        Debug.Log("[RuntimeTarget] Runtime target setup complete.");
    }

    private async Task<Texture2D> DownloadTexture(string url)
    {
        Debug.Log($"[RuntimeTarget] Downloading image: {url}");

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        float lastLoggedProgress = -1f;

        while (!operation.isDone)
        {
            float progress = request.downloadProgress;

            if (progress - lastLoggedProgress >= 0.1f)
            {
                Debug.Log($"[RuntimeTarget] Image download progress: {progress:P0}");
                lastLoggedProgress = progress;
            }

            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[RuntimeTarget] Image download error: {request.error}");
            return null;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);

        if (texture != null)
            texture.name = "DownloadedRuntimeTargetTexture";

        return texture;
    }

    private async Task<string> DownloadVideo(string url)
    {
        Debug.Log($"[RuntimeTarget] Downloading video: {url}");

        string filePath = Path.Combine(Application.persistentDataPath, "runtime_video.mp4");

        if (File.Exists(filePath))
            File.Delete(filePath);

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerFile(filePath);

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        float lastLoggedProgress = -1f;

        while (!operation.isDone)
        {
            float progress = request.downloadProgress;

            if (progress - lastLoggedProgress >= 0.1f)
            {
                Debug.Log($"[RuntimeTarget] Video download progress: {progress:P0}");
                lastLoggedProgress = progress;
            }

            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[RuntimeTarget] Video download error: {request.error}");
            return null;
        }

        Debug.Log($"[RuntimeTarget] Video downloaded to: {filePath}");
        return filePath;
    }

    private void AssignDownloadedTextureToImageTarget(ImageTargetBehaviour target, Texture2D texture)
    {
        Renderer previewRenderer = target.GetComponentInChildren<Renderer>();

        if (previewRenderer != null)
        {
            previewRenderer.material.mainTexture = texture;
            Debug.Log("[RuntimeTarget] Downloaded texture assigned to Image Target preview renderer.");
        }
        else
        {
            Debug.LogWarning("[RuntimeTarget] No preview renderer found on ImageTargetBehaviour.");
        }
    }

    private async Task CreateVideoSurface(Transform parent, Texture2D targetTexture)
    {
        Debug.Log("[RuntimeTarget] Creating video mesh surface...");

        GameObject surface = new GameObject("RuntimeVideoSurface");
        surface.transform.SetParent(parent, false);
        surface.transform.localPosition = new Vector3(0f, surfaceYOffset, 0f);
        surface.transform.localRotation = Quaternion.identity;

        float aspect = (float)targetTexture.height / targetTexture.width;
        float width = targetWidthMeters;
        float height = targetWidthMeters * aspect;

        MeshFilter meshFilter = surface.AddComponent<MeshFilter>();
        videoMeshRenderer = surface.AddComponent<MeshRenderer>();
        videoMeshRenderer.enabled = false;

        meshFilter.mesh = CreateTargetSizedMesh(width, height);

        Material materialInstance = new Material(videoMaterial);
        videoMeshRenderer.material = materialInstance;

        Debug.Log($"[RuntimeTarget] Mesh created: {width}m x {height}m");

        GameObject videoObj = new GameObject("RuntimeVideoPlayer");
        videoObj.transform.SetParent(surface.transform, false);

        videoPlayer = videoObj.AddComponent<VideoPlayer>();
        videoPlayer.source = VideoSource.Url;

        if (downloadVideoBeforePlay)
        {
            string localVideoPath = await DownloadVideo(videoUrl);

            if (string.IsNullOrEmpty(localVideoPath))
            {
                Debug.LogError("[RuntimeTarget] Cannot setup video because download failed.");
                return;
            }

            videoPlayer.url = localVideoPath;
        }
        else
        {
            videoPlayer.url = videoUrl;
        }

        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = videoMeshRenderer;
        videoPlayer.targetMaterialProperty = videoTextureProperty;
        videoPlayer.isLooping = loop;
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;

        Debug.Log($"[RuntimeTarget] Preparing video: {videoPlayer.url}");
        Debug.Log($"[RuntimeTarget] Video material property: {videoTextureProperty}");

        videoPlayer.Prepare();
    }

    private Mesh CreateTargetSizedMesh(float width, float height)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        Mesh mesh = new Mesh { name = "RuntimeTargetVideoMesh" };

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

        mesh.triangles = new[]
        {
            0, 2, 1,
            2, 3, 1
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log("[RuntimeTarget] Video prepared.");

        if (!playOnTargetFound)
        {
            if (videoMeshRenderer != null)
                videoMeshRenderer.enabled = true;

            vp.Play();
        }
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError($"[RuntimeTarget] Video error: {message}");
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        Debug.Log($"[RuntimeTarget] Target status changed: {status.Status}");

        bool tracked =
            status.Status == Status.TRACKED ||
            status.Status == Status.EXTENDED_TRACKED;

        if (videoMeshRenderer != null)
            videoMeshRenderer.enabled = tracked;

        if (videoPlayer == null || !playOnTargetFound)
            return;

        if (tracked)
        {
            Debug.Log("[RuntimeTarget] Target found. Playing video.");

            if (videoPlayer.isPrepared)
                videoPlayer.Play();
            else
                videoPlayer.Prepare();
        }
        else
        {
            Debug.Log("[RuntimeTarget] Target lost. Pausing video.");
            videoPlayer.Pause();
        }
    }

    private void OnDestroy()
    {
        if (imageTarget != null)
            imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }
    }
}