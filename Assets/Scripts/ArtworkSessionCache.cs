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

    public static bool HasVideo(string artworkId) => File.Exists(GetVideoPath(artworkId));

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

        if (File.Exists(localPath))
            File.Delete(localPath);

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerFile(localPath);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogError("[ArtworkSessionCache] Video download failed: " + request.error);
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
