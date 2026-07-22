using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Vuforia;

/// <summary>
/// Legacy/demo helper for a single hardcoded artwork.
/// For the full dynamic server-recognition flow, use ImageScanner + DynamicTracker.
/// </summary>
public class RuntimeVideoTarget : MonoBehaviour
{
    [Header("URLs")]
    [SerializeField] private string imageUrl;
    [SerializeField] private string videoUrl;

    [Header("Target")]
    [SerializeField] private string targetName = "RuntimeImageTarget";
    [SerializeField] private float targetWidthMeters = 0.2f;

    [Header("Video Surface")]
    [SerializeField] private Material videoMaterial;
    [SerializeField] private bool downloadVideoBeforePlay = true;

    private ImageTargetBehaviour imageTarget;
    private ArtworkVideoSurface videoSurface;

    private async void Start()
    {
        await BuildRuntimeTarget();
    }

    public async Task BuildRuntimeTarget()
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(videoUrl))
        {
            Debug.LogError("[RuntimeTarget] Image or video URL is empty.");
            return;
        }

        if (videoMaterial == null)
        {
            Debug.LogError("[RuntimeTarget] Video material is not assigned.");
            return;
        }

        Texture2D targetTexture = await DownloadTexture(imageUrl);
        if (targetTexture == null)
            return;

        if (targetTexture.width > 2048 || targetTexture.height > 2048)
        {
            Debug.LogError($"[RuntimeTarget] Target image too large: {targetTexture.width}x{targetTexture.height}.");
            return;
        }

        if (VuforiaBehaviour.Instance == null)
        {
            Debug.LogError("[RuntimeTarget] VuforiaBehaviour is not available.");
            return;
        }

        imageTarget = await VuforiaBehaviour.Instance.ObserverFactory.CreateImageTargetAsync(
            targetTexture,
            targetWidthMeters,
            targetName
        );

        if (imageTarget == null)
        {
            Debug.LogError("[RuntimeTarget] Failed to create Image Target.");
            return;
        }

        imageTarget.name = targetName;
        imageTarget.transform.SetParent(transform, false);
        AssignMarkerPreview(imageTarget, targetTexture);
        imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;

        GameObject videoRoot = new GameObject("RuntimeVideo");
        videoSurface = videoRoot.AddComponent<ArtworkVideoSurface>();
        
        await videoSurface.SetupAsync(
            imageTarget.transform,
            targetTexture,
            targetWidthMeters,
            videoUrl,
            null,
            videoMaterial
        );
    }

    private static void AssignMarkerPreview(ImageTargetBehaviour target, Texture2D texture)
    {
        Renderer previewRenderer = target.GetComponentInChildren<Renderer>();
        if (previewRenderer != null)
            previewRenderer.material.mainTexture = texture;
    }

    private async Task<Texture2D> DownloadTexture(string url)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
            await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[RuntimeTarget] Image download error: " + request.error);
            return null;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        if (texture != null)
            texture.name = "DownloadedRuntimeTargetTexture";

        return texture;
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        if (videoSurface == null)
            return;

        bool tracked = status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED;
        videoSurface.HandleTrackingChanged(tracked);
    }

    private void OnDestroy()
    {
        if (imageTarget != null)
            imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;
    }
}
