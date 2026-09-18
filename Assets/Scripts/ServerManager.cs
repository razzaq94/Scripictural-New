using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Networking;

public class ServerManager : MonoBehaviour
{
    public static ServerManager Instance;

    [SerializeField] private string serverUrl = "https://your-app.onrender.com";
    [SerializeField] private string uploadFieldName = "file";
    [SerializeField, Range(1, 100)] private int jpegQuality = 100;
    [SerializeField] private bool adaptiveJpegSize = true;
    [SerializeField, Min(1)] private int targetUploadSizeKb = 24;
    [SerializeField, Range(1, 100)] private int minJpegQuality = 55;
    [SerializeField, Range(1, 20)] private int qualityStep = 5;
    [SerializeField] private bool saveUploadedFrames = true;
    [SerializeField, Min(1)] private int maxSavedUploadFrames = 30;

    [FormerlySerializedAs("vuforiaImageScanner")]
    [SerializeField] private ImageScanner imageScanner;

    private static string UploadDebugDir =>
        Path.Combine(Application.persistentDataPath, "upload_debug");

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public IEnumerator SendFrame(Texture2D frame, Action<bool, string, string> onResult)
    {
        yield return SendFrameDetailed(
            frame,
            response =>
            {
                bool matched = response != null &&
                               response.matched &&
                               !string.IsNullOrEmpty(response.artworkId);

                string artworkId = matched ? response.artworkId : null;
                string reason = matched ? "MATCHED" : "NO_MATCH";

                onResult?.Invoke(matched, artworkId, reason);
            },
            error =>
            {
                onResult?.Invoke(false, null, error);
            });
    }

    public IEnumerator SendFrameDetailed(
        Texture2D frame,
        Action<MatchResponse> onSuccess,
        Action<string> onError)
    {
        Debug.Log("Sending request to: " + serverUrl);

        if (frame == null)
        {
            onError?.Invoke("frame is null");
            yield break;
        }

        int rawBytes = frame.width * frame.height * 3; // RGB24
        float rawKb = rawBytes / 1024f;

        int usedQuality;
        byte[] frameBytes = EncodeFrameForUpload(frame, out usedQuality);
        if (frameBytes == null || frameBytes.Length == 0)
        {
            onError?.Invoke("frame bytes are empty");
            yield break;
        }

        float encodedKb = frameBytes.Length / 1024f;
        string uploadLog =
            $" [ServerManager] Upload frame {frame.width}x{frame.height} | raw ~{rawKb:F1} KB | jpg q={usedQuality} => {encodedKb:F1} KB ({frameBytes.Length} bytes)";
        Log(uploadLog);

        string savedBasePath = SaveUploadedJpeg(frameBytes, frame.width, frame.height, usedQuality);

        WWWForm form = new WWWForm();
        form.AddBinaryData(uploadFieldName, frameBytes, "frame.jpg", "image/jpeg");

        using UnityWebRequest req = UnityWebRequest.Post(serverUrl + "/api/recognition/match", form);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string serverBody = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
            string detailedError = $"HTTP {(int)req.responseCode} {req.error}";
            if (!string.IsNullOrWhiteSpace(serverBody))
                detailedError += $" | body: {serverBody}";

            WriteUploadMeta(savedBasePath, frameBytes, frame.width, frame.height, usedQuality, null, "network error: " + detailedError);
            onError?.Invoke("network error: " + detailedError);
            yield break;
        }

        MatchResponse response = null;

        try
        {
            response = JsonConvert.DeserializeObject<MatchResponse>(req.downloadHandler.text);
        }
        catch (Exception ex)
        {
            WriteUploadMeta(savedBasePath, frameBytes, frame.width, frame.height, usedQuality, null, "response parse error: " + ex.Message);
            onError?.Invoke("response parse error: " + ex.Message);
            yield break;
        }

        if (response == null)
        {
            WriteUploadMeta(savedBasePath, frameBytes, frame.width, frame.height, usedQuality, null, "response is null");
            onError?.Invoke("response is null");
            yield break;
        }

        WriteUploadMeta(savedBasePath, frameBytes, frame.width, frame.height, usedQuality, response, null);

        string imageUrl = response.artwork != null ? response.artwork.imageURL : null;
        Log($"[ServerManager] MATCH={response.matched} | artworkId={response.artworkId} | confidence={response.confidence:F3}");
        if (!string.IsNullOrWhiteSpace(imageUrl))
            Log($"[ServerManager] Response image URL: {imageUrl}");
        else
            Log("[ServerManager] Response image URL: (none)");

        onSuccess?.Invoke(response);
    }

    private byte[] EncodeFrameForUpload(Texture2D frame, out int usedQuality)
    {
        usedQuality = Mathf.Clamp(jpegQuality, 1, 100);
        byte[] encoded = frame.EncodeToJPG(usedQuality);

        if (!adaptiveJpegSize || encoded == null || encoded.Length == 0)
            return encoded;

        int targetBytes = Mathf.Max(1, targetUploadSizeKb * 1024);
        int minQuality = Mathf.Clamp(minJpegQuality, 1, usedQuality);
        int step = Mathf.Max(1, qualityStep);

        while (encoded.Length > targetBytes && usedQuality > minQuality)
        {
            usedQuality = Mathf.Max(minQuality, usedQuality - step);
            encoded = frame.EncodeToJPG(usedQuality);

            if (encoded == null || encoded.Length == 0)
                break;
        }

        return encoded;
    }

    private string SaveUploadedJpeg(byte[] frameBytes, int width, int height, int usedQuality)
    {
        if (!saveUploadedFrames || frameBytes == null || frameBytes.Length == 0)
            return null;

        try
        {
            Directory.CreateDirectory(UploadDebugDir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string baseName = $"{stamp}_q{usedQuality}_{width}x{height}_{frameBytes.Length}b";
            string jpgPath = Path.Combine(UploadDebugDir, baseName + ".jpg");
            File.WriteAllBytes(jpgPath, frameBytes);

            Log("[ServerManager] Saved upload JPEG: " + jpgPath);
            TrimOldUploads();
            return Path.Combine(UploadDebugDir, baseName);
        }
        catch (Exception ex)
        {
            Log("[ServerManager] Failed to save upload JPEG: " + ex.Message);
            return null;
        }
    }

    private void WriteUploadMeta(
        string savedBasePath,
        byte[] frameBytes,
        int width,
        int height,
        int usedQuality,
        MatchResponse response,
        string error)
    {
        if (!saveUploadedFrames)
            return;

        string status = !string.IsNullOrEmpty(error)
            ? "ERROR"
            : (response != null && response.matched ? "MATCH" : "NOMATCH");
        string artworkId = response != null && !string.IsNullOrWhiteSpace(response.artworkId)
            ? SanitizeFilePart(response.artworkId)
            : "none";
        string publicName =
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
            "_" + status + "_" + artworkId +
            "_q" + usedQuality + "_" + width + "x" + height + ".jpg";

        string meta =
            "time=" + DateTime.Now.ToString("o") + "\n" +
            "matched=" + (response != null && response.matched) + "\n" +
            "artworkId=" + (response != null ? response.artworkId : "") + "\n" +
            "confidence=" + (response != null ? response.confidence.ToString("F4") : "") + "\n" +
            "imageURL=" + (response != null && response.artwork != null ? response.artwork.imageURL : "") + "\n" +
            "error=" + (error ?? "") + "\n";

        if (!string.IsNullOrWhiteSpace(savedBasePath))
        {
            try
            {
                File.WriteAllText(savedBasePath + ".txt", meta);
            }
            catch (Exception ex)
            {
                Log("[ServerManager] Failed to save upload meta: " + ex.Message);
            }
        }

        SavePublicCopy(frameBytes, publicName, meta);
    }

    private static string SanitizeFilePart(string value)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(bad, chars[i]) >= 0)
                chars[i] = '_';
        }

        string cleaned = new string(chars);
        return cleaned.Length > 40 ? cleaned.Substring(0, 40) : cleaned;
    }

    private void SavePublicCopy(byte[] jpegBytes, string fileName, string metaText)
    {
        if (jpegBytes == null || jpegBytes.Length == 0)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");

            SaveAndroidMedia(resolver, jpegBytes, fileName, "image/jpeg", "Pictures/ScripicturalUploads", true);
            SaveAndroidMedia(resolver, jpegBytes, fileName, "image/jpeg", "Download/ScripicturalUploads", false);
            SaveAndroidMedia(
                resolver,
                System.Text.Encoding.UTF8.GetBytes(metaText ?? string.Empty),
                Path.ChangeExtension(fileName, ".txt"),
                "text/plain",
                "Download/ScripicturalUploads",
                false);

            Log("[ServerManager] Public copy saved. Open Gallery album ScripicturalUploads, or Files > Downloads > ScripicturalUploads");
        }
        catch (Exception ex)
        {
            Log("[ServerManager] Public save failed: " + ex.Message);
        }
#else
        try
        {
            string pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScripicturalUploads");
            Directory.CreateDirectory(pictures);
            File.WriteAllBytes(Path.Combine(pictures, fileName), jpegBytes);
            File.WriteAllText(Path.Combine(pictures, Path.ChangeExtension(fileName, ".txt")), metaText);
            Log("[ServerManager] Public copy saved: " + pictures);
        }
        catch (Exception ex)
        {
            Log("[ServerManager] Public save failed: " + ex.Message);
        }
#endif
    }

    private static void SaveAndroidMedia(
        AndroidJavaObject resolver,
        byte[] bytes,
        string displayName,
        string mimeType,
        string relativePath,
        bool isImage)
    {
        using var values = new AndroidJavaObject("android.content.ContentValues");
        values.Call("put", "_display_name", displayName);
        values.Call("put", "mime_type", mimeType);
        values.Call("put", "relative_path", relativePath);

        AndroidJavaObject collection;
        if (isImage)
        {
            using var images = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            collection = images.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
        }
        else
        {
            using var downloads = new AndroidJavaClass("android.provider.MediaStore$Downloads");
            collection = downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
        }

        using (collection)
        using (AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values))
        {
            if (uri == null)
                throw new Exception("MediaStore insert returned null for " + displayName);

            using AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
            if (stream == null)
                throw new Exception("openOutputStream returned null for " + displayName);

            stream.Call("write", bytes);
            stream.Call("flush");
            stream.Call("close");
        }
    }

    private void TrimOldUploads()
    {
        try
        {
            if (!Directory.Exists(UploadDebugDir))
                return;

            string[] jpgs = Directory.GetFiles(UploadDebugDir, "*.jpg");
            if (jpgs.Length <= maxSavedUploadFrames)
                return;

            Array.Sort(jpgs, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));
            int extra = jpgs.Length - maxSavedUploadFrames;
            for (int i = 0; i < extra; i++)
            {
                File.Delete(jpgs[i]);
                string txt = Path.ChangeExtension(jpgs[i], ".txt");
                if (File.Exists(txt))
                    File.Delete(txt);
            }
        }
        catch (Exception ex)
        {
            Log("[ServerManager] Failed to trim old uploads: " + ex.Message);
        }
    }

    private void Log(string message)
    {
        if (imageScanner != null)
            imageScanner.Log(message);
        else
            Debug.Log(message);
    }

    [Serializable]
    public class MatchResponse
    {
        public bool matched;
        public string artworkId;
        public bool isPublished;
        public float confidence;
        public BoundingBox boundingBox;
        public MatchArtwork artwork;
        public MatchDebug debug;
    }

    [Serializable]
    public class BoundingBox
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [Serializable]
    public class MatchArtwork
    {
        public string videoURL;
        public string imageURL;
        public string compressedVideoUrl;
        public string originalVideoUrl;
        public bool isPublished;
        public ArtworkMetaData metaData;
    }

    [Serializable]
    public class MatchDebug
    {
        public int regionsDetected;
        public int candidatesChecked;
        public int processingMs;
        public MatchTimings timings;
    }

    [Serializable]
    public class MatchTimings
    {
        public int preprocessingMs;
        public int regionDetectionMs;
        public int embeddingMs;
        public int vectorSearchMs;
        public int featureVerifyMs;
        public int totalMs;
    }

    [Serializable]
    public class ArtworkInfo
    {
        public string imageURL;
        public string videoURL;
        public ArtworkMetaData metaData;
    }

    [Serializable]
    public class ArtworkMetaData
    {
        public string title;
        public string description;
        public string artistName;
        public string website;
        public string instagram;
        public string facebook;
    }
}