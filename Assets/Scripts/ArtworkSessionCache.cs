using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class ArtworkSessionCache
{
    [Serializable]
    public class CachedArtworkRecord
    {
        public string artworkId;
        public string imageUrl;
        public string videoUrl;
    }

    [Serializable]
    private class CachedArtworkRecordList
    {
        public List<CachedArtworkRecord> records = new();
    }

    private static string CacheRoot => Path.Combine(Application.persistentDataPath, "artwork_cache");
    private static string ImagesDir => Path.Combine(CacheRoot, "images");
    private static string VideosDir => Path.Combine(CacheRoot, "videos");
    private static string IndexPath => Path.Combine(CacheRoot, "artworks.json");

    public static bool HasImage(string artworkId) => File.Exists(GetImagePath(artworkId));

    public static bool HasVideo(string artworkId) => IsValidVideoFile(GetVideoPath(artworkId));

    public static bool IsValidVideoFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var info = new FileInfo(path);
            if (info.Length < 64)
                return false;

            using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[12];
            if (stream.Read(header, 0, 12) < 12)
                return false;

            // ISO BMFF / MP4: bytes 4..7 == 'ftyp'
            return header[4] == (byte)'f' &&
                   header[5] == (byte)'t' &&
                   header[6] == (byte)'y' &&
                   header[7] == (byte)'p';
        }
        catch
        {
            return false;
        }
    }

    public static string GetImagePath(string artworkId) =>
        Path.Combine(ImagesDir, SanitizeId(artworkId) + ".jpg");

    public static string GetVideoPath(string artworkId) =>
        Path.Combine(VideosDir, SanitizeId(artworkId) + ".mp4");

    public static Texture2D LoadImage(string artworkId)
    {
        string path = GetImagePath(artworkId);
        if (!File.Exists(path))
            return null;

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        texture.name = "CachedMarker_" + artworkId;
        return texture;
    }

    public static void SaveImage(Texture2D texture, string artworkId)
    {
        if (texture == null || string.IsNullOrWhiteSpace(artworkId))
            return;

        EnsureDirectories();
        File.WriteAllBytes(GetImagePath(artworkId), texture.EncodeToJPG(90));
    }

    public static IEnumerator DownloadAndSave(string url, string localPath)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(localPath))
            yield break;

        EnsureDirectories();
        string directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Reuse only if the cached file is a real MP4 (not a truncated/error body).
        if (IsValidVideoFile(localPath))
            yield break;

        if (File.Exists(localPath))
        {
            Debug.LogWarning("[ArtworkSessionCache] Removing invalid cached video: " + localPath);
            File.Delete(localPath);
        }

        string tempPath = localPath + ".tmp";
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 180;
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            yield return request.SendWebRequest();

            bool ok = request.result == UnityWebRequest.Result.Success &&
                      IsValidVideoFile(tempPath);

            if (ok)
            {
                if (File.Exists(localPath))
                    File.Delete(localPath);

                File.Move(tempPath, localPath);

                // Give Android a moment to flush before VideoPlayer opens the file.
                yield return null;
                yield return null;

                if (IsValidVideoFile(localPath))
                {
                    Debug.Log("[ArtworkSessionCache] Video cached (" +
                              new FileInfo(localPath).Length + " bytes): " + localPath);
                    yield break;
                }

                Debug.LogWarning("[ArtworkSessionCache] Cached video failed validation after move.");
                if (File.Exists(localPath))
                    File.Delete(localPath);
            }
            else
            {
                string bodyHint = string.Empty;
                if (File.Exists(tempPath))
                {
                    try
                    {
                        long len = new FileInfo(tempPath).Length;
                        bodyHint = " | bytes=" + len;
                        // If server returned XML/HTML error page, log a snippet.
                        if (len > 0 && len < 2048)
                            bodyHint += " | body=" + File.ReadAllText(tempPath);
                    }
                    catch { /* ignore */ }

                    File.Delete(tempPath);
                }

                Debug.LogWarning(
                    "[ArtworkSessionCache] Video download attempt " + attempt + "/" + maxAttempts +
                    " failed: " + (request.error ?? ("HTTP " + request.responseCode)) + bodyHint);
            }

            if (attempt < maxAttempts)
                yield return new WaitForSecondsRealtime(0.75f * attempt);
        }

        Debug.LogError("[ArtworkSessionCache] Video download failed after retries: " + url);
    }

    public static void UpsertArtworkRecord(string artworkId, string imageUrl, string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkId))
            return;

        EnsureDirectories();
        List<CachedArtworkRecord> records = LoadArtworkRecords();
        CachedArtworkRecord existing = records.Find(r => r.artworkId == artworkId);

        if (existing == null)
        {
            records.Add(new CachedArtworkRecord
            {
                artworkId = artworkId,
                imageUrl = imageUrl,
                videoUrl = videoUrl
            });
        }
        else
        {
            existing.imageUrl = imageUrl;
            existing.videoUrl = videoUrl;
        }

        SaveArtworkRecords(records);
    }

    public static List<CachedArtworkRecord> LoadArtworkRecords()
    {
        if (!File.Exists(IndexPath))
            return new List<CachedArtworkRecord>();

        try
        {
            string json = File.ReadAllText(IndexPath);
            CachedArtworkRecordList wrapper = JsonUtility.FromJson<CachedArtworkRecordList>(json);
            return wrapper?.records ?? new List<CachedArtworkRecord>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ArtworkSessionCache] Failed to read cache index: " + ex.Message);
            return new List<CachedArtworkRecord>();
        }
    }

    public static void RemoveArtworkRecord(string artworkId)
    {
        List<CachedArtworkRecord> records = LoadArtworkRecords();
        records.RemoveAll(r => r.artworkId == artworkId);
        SaveArtworkRecords(records);
    }

    private static void SaveArtworkRecords(List<CachedArtworkRecord> records)
    {
        EnsureDirectories();
        CachedArtworkRecordList wrapper = new CachedArtworkRecordList { records = records };
        File.WriteAllText(IndexPath, JsonUtility.ToJson(wrapper));
    }

    private static void EnsureDirectories()
    {
        if (!Directory.Exists(CacheRoot))
            Directory.CreateDirectory(CacheRoot);
        if (!Directory.Exists(ImagesDir))
            Directory.CreateDirectory(ImagesDir);
        if (!Directory.Exists(VideosDir))
            Directory.CreateDirectory(VideosDir);
    }

    private static string SanitizeId(string artworkId) =>
        artworkId.Replace("/", "_").Replace("\\", "_");
}
