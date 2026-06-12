using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

public class VuforiaArtworkVideoSurface : MonoBehaviour
{
    [SerializeField] private Material videoMaterial;
    [SerializeField] private string videoTextureProperty = "_MainTex";
    [SerializeField] private float surfaceYOffset = 0.002f;
    [SerializeField] private bool playOnTargetFound = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool downloadVideoBeforePlay = true;

    private MeshRenderer videoMeshRenderer;
    private VideoPlayer videoPlayer;

    public bool IsReady { get; private set; }

    public async Task SetupAsync(
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
            return;
        }

        CreateMeshSurface(targetTexture, targetWidthMeters);
        await ConfigureVideoAsync(remoteVideoUrl, localVideoPath);
        IsReady = videoPlayer != null;
    }

    public void SetTracked(bool tracked)
    {
        if (videoMeshRenderer != null)
            videoMeshRenderer.enabled = tracked;

        if (videoPlayer == null || !playOnTargetFound)
            return;

        if (tracked)
        {
            if (videoPlayer.isPrepared)
                videoPlayer.Play();
            else
                videoPlayer.Prepare();
        }
        else
        {
            videoPlayer.Pause();
        }
    }

    private void CreateMeshSurface(Texture2D targetTexture, float targetWidthMeters)
    {
        GameObject surface = new GameObject("VideoSurface");
        surface.transform.SetParent(transform, false);
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

        GameObject videoObj = new GameObject("VideoPlayer");
        videoObj.transform.SetParent(surface.transform, false);

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
        videoPlayer.errorReceived += OnVideoError;
    }

    private async Task ConfigureVideoAsync(string remoteVideoUrl, string localVideoPath)
    {
        if (videoPlayer == null)
            return;

        string playUrl = null;

        if (!string.IsNullOrWhiteSpace(localVideoPath) && File.Exists(localVideoPath))
        {
            playUrl = localVideoPath;
        }
        else if (downloadVideoBeforePlay && !string.IsNullOrWhiteSpace(remoteVideoUrl))
        {
            playUrl = await DownloadVideo(remoteVideoUrl);
        }
        else
        {
            playUrl = remoteVideoUrl;
        }

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            Debug.LogError("[VuforiaArtworkVideoSurface] No valid video URL/path.");
            return;
        }

        videoPlayer.prepareCompleted -= OnVideoPrepared;
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.url = playUrl;
        videoPlayer.Prepare();
    }

    public async Task BindLocalVideoAsync(string localVideoPath)
    {
        if (string.IsNullOrWhiteSpace(localVideoPath) || !File.Exists(localVideoPath))
            return;

        await ConfigureVideoAsync(null, localVideoPath);
    }

    private async Task<string> DownloadVideo(string url)
    {
        string fileName = "runtime_video_" + GetInstanceID() + ".mp4";
        string filePath = Path.Combine(Application.persistentDataPath, fileName);

        if (File.Exists(filePath))
            File.Delete(filePath);

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerFile(filePath);

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone)
            await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[VuforiaArtworkVideoSurface] Video download error: " + request.error);
            return null;
        }

        return filePath;
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
        if (!playOnTargetFound)
        {
            if (videoMeshRenderer != null)
                videoMeshRenderer.enabled = true;
            vp.Play();
        }
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError("[VuforiaArtworkVideoSurface] Video error: " + message);
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
