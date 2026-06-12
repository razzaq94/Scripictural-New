using Newtonsoft.Json;
using System;
using System.Collections;
using UnityEngine;
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

    [SerializeField] private VuforiaImageScanner vuforiaImageScanner;

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
            onError?.Invoke("response parse error: " + ex.Message);
            yield break;
        }

        if (response == null)
        {
            onError?.Invoke("response is null");
            yield break;
        }

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

    private void Log(string message)
    {
        if (vuforiaImageScanner != null)
            vuforiaImageScanner.Log(message);
        else
            Debug.Log(message);
    }

    [Serializable]
    public class MatchResponse
    {
        public bool matched;
        public string artworkId;
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