using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Vuforia;
using Image = Vuforia.Image;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class VuforiaImageScanner : MonoBehaviour
{
    private static readonly PixelFormat[] PreferredCameraFormats =
    {
        PixelFormat.RGB888,
        PixelFormat.RGBA8888
    };

    private const TextureFormat ProcessingTextureFormat = TextureFormat.RGB24;

    [Header("References")]
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private VuforiaDynamicTracker vuforiaDynamicTracker;

    [Header("Scan Settings")]
    [SerializeField] private float scanInterval = 1f;
    [SerializeField] private bool useOriginalCameraResolution = true;
    [SerializeField] private int processingWidth = 320;
    [SerializeField] private int processingHeight = 240;
    [SerializeField] private float sendCooldown = 0.5f;
    [SerializeField] private int qualitySampleStride = 2;
    [SerializeField, Range(1f, 3f)] private float serverFrameZoom = 1.8f;

    [Header("Filter Thresholds")]
    [SerializeField] private float edgeThreshold = 0.002f;
    [SerializeField] private float colorVarThreshold = 0.005f;
    [SerializeField, Range(0.7f, 0.99f)] private float frameSimilarityThreshold = 0.9f;
    [SerializeField, Range(4, 16)] private int fingerprintGridSize = 8;

    [Header("Orientation")]
    [SerializeField] private bool normalizePortraitOrientation = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;
    [SerializeField] private bool saveDebugFrames = false;

    [SerializeField] GameObject splashCanvas;

    private float timer;
    private bool isProcessing;
    private bool formatRegistered;
    private bool vuforiaReady;
    private bool hasCachedCameraFrame;
    private bool hasVideoBackgroundFrame;
    private PixelFormat activeCameraFormat = PixelFormat.UNKNOWN_FORMAT;
    private string lastCaptureSource = "none";
    private int framesSinceFormatRegistration;
    private float lastSendTime = -999f;
    private readonly List<string> logLines = new List<string>();
    private const int MaxLogLines = 14;
    private Texture2D processingTexture;
    private Texture2D cameraTexture;
    private Texture2D videoBackgroundTexture;
    private bool scannerBlockedByTracking;
    private ulong lastFrameFingerprint;
    private bool hasLastFrameFingerprint;
    private float lastDuplicateLogTime = -999f;
    private int fingerprintBitCount = 64;
    private bool isRequestingPermission;

    private void Start()
    {
        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped += OnVuforiaStopped;

        if (VuforiaApplication.Instance.IsRunning)
            OnVuforiaStarted();

        Log($"Scanner started | interval={scanInterval:F2}s | cooldown={sendCooldown:F2}s");
    }

    private void OnDestroy()
    {
        DetachVuforiaHooks();

        VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnVuforiaStopped;

        if (VuforiaApplication.Instance.IsRunning && formatRegistered)
            UnregisterCameraFormat();

        if (processingTexture != null)
            Destroy(processingTexture);

        if (cameraTexture != null)
            Destroy(cameraTexture);

        if (videoBackgroundTexture != null)
            Destroy(videoBackgroundTexture);
    }

    private void OnVuforiaStarted()
    {

        cameraTexture = new Texture2D(4, 4, ProcessingTextureFormat, false);
        videoBackgroundTexture = new Texture2D(4, 4, ProcessingTextureFormat, false);
        RegisterCameraFormat();
        AttachVuforiaHooks();
        vuforiaReady = true;
        framesSinceFormatRegistration = 0;
        Log($"Vuforia ready | formatRegistered={formatRegistered} | format={activeCameraFormat}");

        StartCoroutine(HideSplash());
    }
    private IEnumerator HideSplash()
    {
        yield return new WaitForSeconds(1.5f);
        splashCanvas.SetActive(false);

    }
    private void OnVuforiaStopped()
    {
        DetachVuforiaHooks();
        UnregisterCameraFormat();
        vuforiaReady = false;
        hasCachedCameraFrame = false;
        hasVideoBackgroundFrame = false;
        framesSinceFormatRegistration = 0;

        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
            cameraTexture = null;
        }

        if (videoBackgroundTexture != null)
        {
            Destroy(videoBackgroundTexture);
            videoBackgroundTexture = null;
        }
    }

    private void AttachVuforiaHooks()
    {
        if (VuforiaBehaviour.Instance == null)
            return;

        VuforiaBehaviour.Instance.World.OnStateUpdated -= OnVuforiaUpdated;
        VuforiaBehaviour.Instance.World.OnStateUpdated += OnVuforiaUpdated;

        if (VuforiaBehaviour.Instance.VideoBackground != null)
        {
            VuforiaBehaviour.Instance.VideoBackground.OnVideoBackgroundChanged -= OnVideoBackgroundChanged;
            VuforiaBehaviour.Instance.VideoBackground.OnVideoBackgroundChanged += OnVideoBackgroundChanged;
        }
    }

    private void DetachVuforiaHooks()
    {
        if (VuforiaBehaviour.Instance == null)
            return;

        VuforiaBehaviour.Instance.World.OnStateUpdated -= OnVuforiaUpdated;

        if (VuforiaBehaviour.Instance.VideoBackground != null)
            VuforiaBehaviour.Instance.VideoBackground.OnVideoBackgroundChanged -= OnVideoBackgroundChanged;
    }

    private void OnVideoBackgroundChanged()
    {
        hasVideoBackgroundFrame = true;
        CacheVideoBackgroundFrame();
    }

    private void OnVuforiaUpdated()
    {
        if (!vuforiaReady || VuforiaBehaviour.Instance == null)
            return;

        framesSinceFormatRegistration++;

        if (formatRegistered && cameraTexture != null)
            CacheCameraDeviceFrame();

        CacheVideoBackgroundFrame();
    }

    private void CacheCameraDeviceFrame()
    {
        try
        {
            Image cameraImage = VuforiaBehaviour.Instance.CameraDevice.GetCameraImage(activeCameraFormat);
            if (Image.IsNullOrEmpty(cameraImage))
                return;

            cameraImage.CopyToTexture(cameraTexture, true);
            hasCachedCameraFrame = true;
        }
        catch (System.Exception ex)
        {
            if (verboseLogs)
                Debug.LogWarning("[VuforiaScanner] CameraDevice capture failed: " + ex.Message);
        }
    }

    private void CacheVideoBackgroundFrame()
    {
        Texture source = VuforiaBehaviour.Instance?.VideoBackground?.VideoBackgroundTexture;
        if (source == null || source.width <= 16 || source.height <= 16 || videoBackgroundTexture == null)
            return;

        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            if (videoBackgroundTexture.width != source.width || videoBackgroundTexture.height != source.height)
            {
                videoBackgroundTexture.Reinitialize(source.width, source.height);
                videoBackgroundTexture.Apply(false, false);
            }

            videoBackgroundTexture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            videoBackgroundTexture.Apply(false, false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            hasVideoBackgroundFrame = true;
        }
        catch (System.Exception ex)
        {
            if (verboseLogs)
                Debug.LogWarning("[VuforiaScanner] VideoBackground capture failed: " + ex.Message);
        }
    }

    private void RegisterCameraFormat()
    {
        if (VuforiaBehaviour.Instance == null)
        {
            formatRegistered = false;
            activeCameraFormat = PixelFormat.UNKNOWN_FORMAT;
            return;
        }

        foreach (PixelFormat format in PreferredCameraFormats)
        {
            if (VuforiaBehaviour.Instance.CameraDevice.SetFrameFormat(format, true))
            {
                activeCameraFormat = format;
                formatRegistered = true;
                return;
            }
        }

        formatRegistered = false;
        activeCameraFormat = PixelFormat.UNKNOWN_FORMAT;
        Log("<color=#ffaa00>Camera pixel format registration failed. Will use VideoBackground texture.</color>");
    }

    private void UnregisterCameraFormat()
    {
        if (VuforiaBehaviour.Instance == null || !formatRegistered || activeCameraFormat == PixelFormat.UNKNOWN_FORMAT)
            return;

        VuforiaBehaviour.Instance.CameraDevice.SetFrameFormat(activeCameraFormat, false);
        formatRegistered = false;
        activeCameraFormat = PixelFormat.UNKNOWN_FORMAT;
    }

    private void Update()
    {
        if (!HasCameraPermission())
        {
            RefreshCameraPermissionState();
            return;
        }

        if (!vuforiaReady)
        {
            if (verboseLogs && Time.frameCount % 120 == 0)
                Log("<color=#ffaa00>Waiting for Vuforia to start...</color>");
            return;
        }

        bool shouldBlockScanner = IsScannerBlocked();
        HandleTrackingBlockState(shouldBlockScanner);
        if (shouldBlockScanner)
            return;

        timer += Time.deltaTime;
        if (timer >= scanInterval && !isProcessing)
        {
            timer = 0f;
            StartCoroutine(ScanFrame());
        }
    }

    private IEnumerator ScanFrame()
    {
        isProcessing = true;

        if (!HasCameraPermission())
        {
            isProcessing = false;
            yield break;
        }

        if (IsScannerBlocked())
        {
            isProcessing = false;
            yield break;
        }

        yield return new WaitForEndOfFrame();

        if (!isActiveAndEnabled)
        {
            isProcessing = false;
            yield break;
        }

        Texture2D frame = CaptureFrame();
        if (frame == null)
        {
            Log("<color=#ff4444>Frame capture failed - no frame sent</color>");
            isProcessing = false;
            yield break;
        }

        Log($"<color=#888888>Captured {frame.width}x{frame.height} via {lastCaptureSource}</color>");

        float edgeVal = GetEdgeDensity(frame);
        float colorVal = GetColorVariance(frame);
        bool edgePass = edgeVal > edgeThreshold;
        bool colorPass = colorVal > colorVarThreshold;
        bool passes = edgePass && colorPass;
        bool cooldownReady = (Time.time - lastSendTime) >= sendCooldown;
        ulong currentFingerprint = ComputeFrameFingerprint(frame);
        float similarityToLast = hasLastFrameFingerprint
            ? GetFingerprintSimilarity(lastFrameFingerprint, currentFingerprint)
            : 0f;
        bool isDuplicateFrame = hasLastFrameFingerprint && similarityToLast >= frameSimilarityThreshold;

        if (passes && cooldownReady && !isDuplicateFrame)
        {
            if (ServerManager.Instance == null)
            {
                Log("<color=#ff4444>ServerManager not ready yet</color>");
                isProcessing = false;
                yield break;
            }

            if (saveDebugFrames)
                SaveDebugFrame(frame);

            yield return StartCoroutine(
                ServerManager.Instance.SendFrameDetailed(
                    frame,
                    response =>
                    {
                        if (response != null &&
                            response.matched &&
                            !string.IsNullOrWhiteSpace(response.artworkId))
                        {
                            Log($"<color=#00ffcc>MATCH: {response.artworkId}</color>");

                            if (vuforiaDynamicTracker != null)
                                vuforiaDynamicTracker.OnArtworkDetected(response);
                            else
                                Log("<color=#ff4444>VuforiaDynamicTracker reference missing</color>");
                        }
                        else
                        {
                            string debugInfo = FormatNoMatchDebug(response);
                            Log("<color=#ff4444>No match</color>" + debugInfo);
                        }
                    },
                    error => Log($"<color=#ff4444>Request failed - {error}</color>")
                )
            );

            lastSendTime = Time.time;
            lastFrameFingerprint = currentFingerprint;
            hasLastFrameFingerprint = true;
        }
        else if (passes && !cooldownReady && verboseLogs)
        {
            float left = sendCooldown - (Time.time - lastSendTime);
            Log($"<color=#ffaa66>Skipped - cooldown {left:F1}s</color>");
        }
        else if (passes && isDuplicateFrame && (verboseLogs || Time.time - lastDuplicateLogTime > 1.5f))
        {
            Log($"<color=#ffaa66>Skipped - similar frame ({similarityToLast * 100f:F0}%)</color>");
            lastDuplicateLogTime = Time.time;
        }
        else if (!passes)
        {
            string why = (!edgePass ? "low-edges " : "") + (!colorPass ? "low-color" : "");
            Log($"<color=#ff8800>Skipped - {why.Trim()}</color>");
        }

        isProcessing = false;
    }

    private Texture2D CaptureFrame()
    {
        if (!HasCameraPermission())
            return null;

        if (!vuforiaReady)
        {
            if (verboseLogs)
                Log("<color=#ffaa00>Vuforia not ready yet</color>");
            return null;
        }

        if (formatRegistered && hasCachedCameraFrame && cameraTexture != null && cameraTexture.width > 0)
        {
            lastCaptureSource = "vuforia-camera";
            return BuildProcessingFrame(cameraTexture, rotateForRawCamera: true);
        }

        if (formatRegistered && VuforiaBehaviour.Instance != null && cameraTexture != null)
        {
            CacheCameraDeviceFrame();
            if (hasCachedCameraFrame && cameraTexture.width > 0)
            {
                lastCaptureSource = "vuforia-poll";
                return BuildProcessingFrame(cameraTexture, rotateForRawCamera: true);
            }
        }

        CacheVideoBackgroundFrame();
        if (hasVideoBackgroundFrame && videoBackgroundTexture != null && videoBackgroundTexture.width > 0)
        {
            lastCaptureSource = "vuforia-video-background";
            return BuildProcessingFrame(videoBackgroundTexture, rotateForRawCamera: false);
        }

        lastCaptureSource = "screen-fallback";
        Log("<color=#ffaa00>WARNING: Vuforia camera unavailable. Using screen fallback.</color>");
        if (verboseLogs)
        {
            Log($"<color=#ffaa00>ready={vuforiaReady} format={formatRegistered}/{activeCameraFormat} " +
                $"cachedCamera={hasCachedCameraFrame} cachedVB={hasVideoBackgroundFrame} " +
                $"warmupFrames={framesSinceFormatRegistration}</color>");
        }

        return CaptureScreenFallback();
    }

    private Texture2D BuildProcessingFrame(Texture2D source, bool rotateForRawCamera)
    {
        Texture2D oriented = source;
        Texture2D rotatedTemp = null;

        if (rotateForRawCamera &&
            normalizePortraitOrientation &&
            ShouldRotateToPortrait(source.width, source.height))
        {
            rotatedTemp = RotateTexture90Clockwise(source);
            oriented = rotatedTemp;
        }

        RectInt cropRect = ResolveInputRect(oriented.width, oriented.height);
        Vector2Int outputSize = ResolveOutputDimensions(cropRect.width, cropRect.height);
        EnsureProcessingTexture(outputSize.x, outputSize.y);

        Color[] pixels = oriented.GetPixels(
            cropRect.x,
            cropRect.y,
            cropRect.width,
            cropRect.height
        );

        if (cropRect.width != outputSize.x || cropRect.height != outputSize.y)
        {
            Texture2D cropped = new Texture2D(cropRect.width, cropRect.height, ProcessingTextureFormat, false);
            cropped.SetPixels(pixels);
            cropped.Apply(false, false);

            RenderTexture rt = RenderTexture.GetTemporary(outputSize.x, outputSize.y, 0);
            Graphics.Blit(cropped, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            processingTexture.ReadPixels(new Rect(0, 0, outputSize.x, outputSize.y), 0, 0);
            processingTexture.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(cropped);
        }
        else
        {
            processingTexture.SetPixels(pixels);
            processingTexture.Apply(false, false);
        }

        if (rotatedTemp != null)
            Destroy(rotatedTemp);

        return processingTexture;
    }

    private static bool ShouldRotateToPortrait(int width, int height)
    {
        bool inputLandscape = width >= height;
        bool screenPortrait = Screen.height >= Screen.width;
        return inputLandscape && screenPortrait;
    }

    private static Texture2D RotateTexture90Clockwise(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        Color32[] src = source.GetPixels32();
        int newWidth = height;
        int newHeight = width;
        Color32[] dst = new Color32[src.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIndex = y * width + x;
                int newX = height - 1 - y;
                int newY = x;
                dst[newY * newWidth + newX] = src[srcIndex];
            }
        }

        Texture2D rotated = new Texture2D(newWidth, newHeight, source.format, false);
        rotated.SetPixels32(dst);
        rotated.Apply(false, false);
        return rotated;
    }

    private static string FormatNoMatchDebug(ServerManager.MatchResponse response)
    {
        if (response == null)
            return " (null response)";

        string debug = $" | confidence={response.confidence:F3}";
        if (response.debug != null)
            debug += $" | regions={response.debug.regionsDetected} | candidates={response.debug.candidatesChecked}";

        return debug;
    }

    private static void SaveDebugFrame(Texture2D frame)
    {
        byte[] jpg = frame.EncodeToJPG(90);
        string path = System.IO.Path.Combine(
            Application.persistentDataPath,
            $"vuforia_scan_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
        System.IO.File.WriteAllBytes(path, jpg);
        Debug.Log("[VuforiaScanner] Debug frame saved: " + path);
    }

    private Texture2D CaptureScreenFallback()
    {
        RenderTexture rt = RenderTexture.GetTemporary(processingWidth, processingHeight, 0);
        ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        EnsureProcessingTexture(processingWidth, processingHeight);
        processingTexture.ReadPixels(new Rect(0, 0, processingWidth, processingHeight), 0, 0);
        processingTexture.Apply(false, false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return processingTexture;
    }

    private RectInt ResolveInputRect(int inputWidth, int inputHeight)
    {
        float zoom = Mathf.Max(1f, serverFrameZoom);
        if (zoom <= 1.001f)
            return new RectInt(0, 0, inputWidth, inputHeight);

        int cropWidth = Mathf.Clamp(Mathf.RoundToInt(inputWidth / zoom), 1, inputWidth);
        int cropHeight = Mathf.Clamp(Mathf.RoundToInt(inputHeight / zoom), 1, inputHeight);
        int x = (inputWidth - cropWidth) / 2;
        int y = (inputHeight - cropHeight) / 2;
        return new RectInt(x, y, cropWidth, cropHeight);
    }

    private Vector2Int ResolveOutputDimensions(int inputWidth, int inputHeight)
    {
        if (useOriginalCameraResolution)
            return new Vector2Int(inputWidth, inputHeight);

        int requestedWidth = Mathf.Max(1, processingWidth);
        int requestedHeight = Mathf.Max(1, processingHeight);

        bool requestedLandscape = requestedWidth >= requestedHeight;
        bool inputLandscape = inputWidth >= inputHeight;
        if (requestedLandscape != inputLandscape)
        {
            int temp = requestedWidth;
            requestedWidth = requestedHeight;
            requestedHeight = temp;
        }

        if (requestedWidth > inputWidth || requestedHeight > inputHeight)
        {
            float scale = Mathf.Min((float)inputWidth / requestedWidth, (float)inputHeight / requestedHeight);
            requestedWidth = Mathf.Max(1, Mathf.FloorToInt(requestedWidth * scale));
            requestedHeight = Mathf.Max(1, Mathf.FloorToInt(requestedHeight * scale));
        }

        return new Vector2Int(requestedWidth, requestedHeight);
    }

    private void EnsureProcessingTexture(int width, int height)
    {
        if (processingTexture != null && processingTexture.width == width && processingTexture.height == height)
            return;

        if (processingTexture != null)
            Destroy(processingTexture);

        processingTexture = new Texture2D(width, height, ProcessingTextureFormat, false);
        processingTexture.wrapMode = TextureWrapMode.Clamp;
        processingTexture.filterMode = FilterMode.Bilinear;
    }

    private float GetEdgeDensity(Texture2D tex)
    {
        var raw = tex.GetRawTextureData<byte>();
        int w = tex.width, h = tex.height;
        int stride = Mathf.Max(1, qualitySampleStride);

        float[] gray = new float[w * h];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 3)
            gray[i] = (0.114f * raw[p] + 0.587f * raw[p + 1] + 0.299f * raw[p + 2]) / 255f;

        float sumSq = 0f;
        int count = 0;

        for (int y = 1; y < h - 1; y += stride)
        {
            for (int x = 1; x < w - 1; x += stride)
            {
                float gx = -gray[(y - 1) * w + (x - 1)] + gray[(y - 1) * w + (x + 1)]
                         - 2f * gray[y * w + (x - 1)] + 2f * gray[y * w + (x + 1)]
                         - gray[(y + 1) * w + (x - 1)] + gray[(y + 1) * w + (x + 1)];

                float gy = -gray[(y - 1) * w + (x - 1)] - 2f * gray[(y - 1) * w + x] - gray[(y - 1) * w + (x + 1)]
                         + gray[(y + 1) * w + (x - 1)] + 2f * gray[(y + 1) * w + x] + gray[(y + 1) * w + (x + 1)];

                sumSq += gx * gx + gy * gy;
                count++;
            }
        }

        return count > 0 ? sumSq / count : 0f;
    }

    private float GetColorVariance(Texture2D tex)
    {
        var raw = tex.GetRawTextureData<byte>();
        int stride = Mathf.Max(1, qualitySampleStride);
        int pixelCount = tex.width * tex.height;

        float rM = 0f, gM = 0f, bM = 0f;
        int sampled = 0;

        for (int i = 0; i < pixelCount; i += stride)
        {
            int idx = i * 3;
            bM += raw[idx];
            gM += raw[idx + 1];
            rM += raw[idx + 2];
            sampled++;
        }

        if (sampled == 0)
            return 0f;

        rM /= sampled;
        gM /= sampled;
        bM /= sampled;

        float v = 0f;
        for (int i = 0; i < pixelCount; i += stride)
        {
            int idx = i * 3;
            float b = raw[idx];
            float g = raw[idx + 1];
            float r = raw[idx + 2];
            v += (r - rM) * (r - rM) + (g - gM) * (g - gM) + (b - bM) * (b - bM);
        }

        return (v / sampled) / (255f * 255f);
    }

    private ulong ComputeFrameFingerprint(Texture2D tex)
    {
        var raw = tex.GetRawTextureData<byte>();
        int width = tex.width;
        int height = tex.height;
        int grid = Mathf.Max(4, fingerprintGridSize);
        int samples = grid * grid;
        float totalLuma = 0f;
        float[] lumaValues = new float[samples];
        int index = 0;

        for (int gy = 0; gy < grid; gy++)
        {
            int y = Mathf.Clamp((gy * height) / grid, 0, height - 1);
            for (int gx = 0; gx < grid; gx++)
            {
                int x = Mathf.Clamp((gx * width) / grid, 0, width - 1);
                int rawIdx = (y * width + x) * 3;
                float luma = 0.299f * raw[rawIdx + 2] + 0.587f * raw[rawIdx + 1] + 0.114f * raw[rawIdx];
                lumaValues[index++] = luma;
                totalLuma += luma;
            }
        }

        float avg = totalLuma / samples;
        ulong signature = 0UL;
        int bitCount = Mathf.Min(64, samples);
        fingerprintBitCount = Mathf.Max(1, bitCount);

        for (int i = 0; i < bitCount; i++)
        {
            if (lumaValues[i] >= avg)
                signature |= 1UL << i;
        }

        return signature;
    }

    private float GetFingerprintSimilarity(ulong first, ulong second)
    {
        ulong xor = first ^ second;
        int distanceBits = CountSetBits(xor);
        return 1f - (distanceBits / (float)Mathf.Max(1, fingerprintBitCount));
    }

    private static int CountSetBits(ulong value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private bool IsScannerBlocked()
    {
        if (ChatManager.instance != null && ChatManager.instance.IsChatOpen)
            return true;

        return vuforiaDynamicTracker != null && vuforiaDynamicTracker.ShouldBlockScanner();
    }

    private void HandleTrackingBlockState(bool shouldBlockScanner)
    {
        if (shouldBlockScanner == scannerBlockedByTracking)
            return;

        bool wasBlocked = scannerBlockedByTracking;
        scannerBlockedByTracking = shouldBlockScanner;

        if (wasBlocked && !scannerBlockedByTracking)
            ResetScanDeduplication();

        if (scannerBlockedByTracking)
        {
            if (debugText != null)
                debugText.text = string.Empty;
        }
        else if (debugText != null)
        {
            debugText.text = string.Join("\n", logLines);
        }
    }

    private void ResetScanDeduplication()
    {
        hasLastFrameFingerprint = false;
        lastFrameFingerprint = 0;
        lastSendTime = -999f;
        timer = scanInterval;

        if (verboseLogs)
            Log("<color=#888888>Scanner resumed - dedup reset</color>");
    }

    public void Log(string msg)
    {
        //Debug.Log("[VuforiaScanner] " + msg);

        string stripped = msg;
        const string colorOpen = "<color=";
        int colorIdx = stripped.IndexOf(colorOpen, System.StringComparison.Ordinal);
        if (colorIdx >= 0)
        {
            int close = stripped.IndexOf('>', colorIdx);
            if (close >= 0)
                stripped = stripped.Substring(close + 1);
        }

        stripped = stripped.Replace("</color>", string.Empty);
        logLines.Add(stripped);
        while (logLines.Count > MaxLogLines)
            logLines.RemoveAt(0);

        if (!scannerBlockedByTracking && debugText != null)
            debugText.text = string.Join("\n", logLines);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        isProcessing = false;
        isRequestingPermission = false;
    }

    private bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
        return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
    }

    private void RefreshCameraPermissionState()
    {
        if (HasCameraPermission() || isRequestingPermission)
            return;

        isRequestingPermission = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        Permission.RequestUserPermission(Permission.Camera);
        StartCoroutine(WaitForAndroidPermissionResult());
#else
        StartCoroutine(RequestWebcamPermissionCoroutine());
#endif
    }

    private IEnumerator RequestWebcamPermissionCoroutine()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        isRequestingPermission = false;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator WaitForAndroidPermissionResult()
    {
        float timeoutAt = Time.realtimeSinceStartup + 10f;
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        while (!Application.isFocused)
            yield return null;

        yield return null;
        isRequestingPermission = false;
    }
#endif
}
