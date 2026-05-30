using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using PhiInfo.Processing.DataProvider;
using Shua.UA.Core.Asset;
using Shua.Zip;
using Shua.Zip.ReadAt;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhiStore.Addons.PhiInfo;

/// <summary>
/// 共享的资源类型枚举，供 service 与 API 共用，避免重复定义。
/// </summary>
public enum ResourceType : int
{
    Illustration = 0,
    IllustrationBlur = 1,
    IllustrationLowRes = 2,
    Music = 3,
    Chart = 4,
    CollectionAsset = 5,
    Avatar = 6
}

public class PhiInfoService : IDisposable
{
    private PhiInfoContext? _context;

    public PhiInfoContext? Context => _context;

    private static IDataProvider CreateProvider(string[] paths, string cldbPath, bool isWeb)
    {
        var readAts = paths
            .Select(path => isWeb
                ? (IReadAt)new HttpReadAt(path)
                : new MmapReadAt(path))
            .ToArray();
        var zips = readAts.Select(r => new ShuaZip(r)).ToArray();
        var cldbStream = File.OpenRead(cldbPath);
        return new AndroidPackagesDataProvider(zips, cldbStream);
    }

    public Language[] GetSupportedLanguages()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return Enum.GetValues<Language>();
    }

    public List<SongInfo> GetSongsData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractSongs();
    }

    public List<Folder> GetCollectionData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractCollection();
    }

    public List<Avatar> GetAvatarsData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractAvatars();
    }

    public List<ChapterInfo> GetChaptersData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractChapters();
    }

    public Dictionary<string, string> GetAssetCatalogData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return new Dictionary<string, string>(_context.Asset.Catalog);
    }

    public T GetAsset<T>(string assetPath) where T : UnityAsset, new()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        var catalog = _context.Asset.Catalog;
        if (!catalog.TryGetValue(assetPath, out var rawPath))
        {
            rawPath = catalog.FirstOrDefault(kvp => kvp.Key.Equals(assetPath, StringComparison.OrdinalIgnoreCase)).Value
                ?? throw new FileNotFoundException($"Catalog missing tracking for {assetPath}");
        }
        return _context.Asset.Get<T>(rawPath);
    }

    public Dictionary<Language, List<string>> GetTipsData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractTips();
    }

    public AllInfo GetAllInfoData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.ExtractAllInfo();
    }

    public PhiVersion GetPhiVersionData()
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized");
        return _context.Info.GetPhiVersion();
    }

    private static PhiInfoContext CreateContext(IDataProvider provider)
    {
        try
        {
            return new PhiInfoContext(provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public void FreeContext()
    {
        _context?.Dispose();
        _context = null;
    }

    public void InitFromLocalApk(string[] apkPaths, string cldbPath)
    {
        FreeContext();
        _context = CreateContext(CreateProvider(apkPaths, cldbPath, false));
    }

    public void InitFromWebApk(string[] apkUrls, string cldbPath)
    {
        FreeContext();
        _context = CreateContext(CreateProvider(apkUrls, cldbPath, true));
    }

    public void InitFromLocalApkAsync(string apkPath, string cldbPath, Action<int, float>? progress = null, Action<bool, string>? completed = null)
    {
        InitAsync(() => CreateProvider(new[] { apkPath }, cldbPath, false), progress, completed);
    }

    public void InitFromWebApkAsync(string apkUrl, string cldbPath, Action<int, float>? progress = null, Action<bool, string>? completed = null)
    {
        InitAsync(() => CreateProvider(new[] { apkUrl }, cldbPath, true), progress, completed);
    }

    private void InitAsync(Func<IDataProvider> providerFactory, Action<int, float>? progress = null, Action<bool, string>? completed = null)
    {
        Task.Run(() =>
        {
            FreeContext();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    progress?.Invoke((int)0, 0.1f);
                    var dp = providerFactory();
                    progress?.Invoke((int)1, 0.6f);
                    _context = CreateContext(dp);
                    progress?.Invoke((int)2, 1.0f);
                    completed?.Invoke(true, string.Empty);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        completed?.Invoke(false, ex.Message);
                    }
                    System.Threading.Thread.Sleep(500);
                }
            }
        });
    }

    public void Dispose()
    {
        FreeContext();
    }

    // --- TapTap APK helper ---
    private static readonly HttpClient Client = new HttpClient();
    private static readonly Random Rnd = new Random();

    private const string XUATemplate = "V=1&PN=TapTap&VN=2.40.1-rel.100000&VN_CODE=240011000&LOC=CN&LANG=zh_CN&CH=default&UID={0}&NT=1&SR=1080x2030&DEB=Xiaomi&DEM=Redmi+Note+5&OSV=9";
    private const string SignSalt = "PeCkE6Fu0B10Vm9BKfPfANwCUAn5POcs";
    private const string NonceChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    public int DefaultAppId { get; set; } = 165287;

    private static string ComputeMd5(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder();
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }

    private static string CreateNonce()
    {
        var chars = new char[5];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = NonceChars[Rnd.Next(NonceChars.Length)];
        }
        return new string(chars);
    }

    public async Task<ApkInfo> GetApkInfoAsync(int appId)
    {
        var uid = Guid.NewGuid().ToString();
        var xUa = string.Format(XUATemplate, uid);
        var encodedXUa = Uri.EscapeDataString(xUa);

        var url1 = $"https://api.taptapdada.com/app/v2/detail-by-id/{appId}?X-UA={encodedXUa}";
        using var req1 = new HttpRequestMessage(HttpMethod.Get, url1);
        req1.Headers.Add("User-Agent", "okhttp/3.12.1");
        using var res1 = await Client.SendAsync(req1);
        res1.EnsureSuccessStatusCode();
        var raw1 = await res1.Content.ReadAsStringAsync();

        using var doc1 = JsonDocument.Parse(raw1);
        var apkId = doc1.RootElement.GetProperty("data")
            .GetProperty("download")
            .GetProperty("apk_id")
            .GetInt64();

        var nonce = CreateNonce();
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var param = $"abi=arm64-v8a,armeabi-v7a,armeabi&id={apkId}&node={uid}&nonce={nonce}&sandbox=1&screen_densities=xhdpi&time={time}";
        var sign = ComputeMd5($"X-UA={xUa}&{param}{SignSalt}");
        var body = $"{param}&sign={sign}";

        var url2 = $"https://api.taptapdada.com/apk/v1/detail?X-UA={encodedXUa}";
        using var req2 = new HttpRequestMessage(HttpMethod.Post, url2);
        req2.Headers.Add("User-Agent", "okhttp/3.12.1");
        req2.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var res2 = await Client.SendAsync(req2);
        res2.EnsureSuccessStatusCode();
        var raw2 = await res2.Content.ReadAsStringAsync();

        using var doc2 = JsonDocument.Parse(raw2);
        var dataNode = doc2.RootElement.GetProperty("data");

        string? downloadUrl = null;
        string? fileName = null;
        int versionCode = 0;
        string? versionName = null;

        if (dataNode.TryGetProperty("apk", out var apkNode))
        {
            downloadUrl = apkNode.GetProperty("download").GetString();
            fileName = apkNode.GetProperty("name").GetString();
            versionCode = apkNode.GetProperty("version_code").GetInt32();
            versionName = apkNode.GetProperty("version_name").GetString();
        }
        else if (dataNode.TryGetProperty("download", out var directDownloadNode))
        {
            downloadUrl = directDownloadNode.GetString();
            using var doc1Again = JsonDocument.Parse(raw1);
            var apkData = doc1Again.RootElement.GetProperty("data").GetProperty("download").GetProperty("apk");
            fileName = apkData.GetProperty("name").GetString();
            versionCode = apkData.GetProperty("version_code").GetInt32();
            versionName = apkData.GetProperty("version_name").GetString();
        }
        else
        {
            throw new Exception("无法从返回数据中找到下载链接");
        }

        return new ApkInfo
        {
            DownloadUrl = downloadUrl,
            FileName = fileName,
            VersionCode = versionCode,
            VersionName = versionName
        };
    }

    public sealed class ApkInfo
    {
        public string? DownloadUrl { get; set; }
        public string? FileName { get; set; }
        public int VersionCode { get; set; }
        public string? VersionName { get; set; }
    }

    public static int? ExtractVersionCodeFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = Regex.Match(fileName, @"-(\d+)\.apk$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var code))
        {
            return code;
        }

        return null;
    }

    public static string? FindLocalApkWithHighestVersion(string? cloudFileName)
    {
        if (string.IsNullOrWhiteSpace(cloudFileName))
        {
            return null;
        }

        var match = Regex.Match(cloudFileName, @"^(.+?)-(\d+)\.apk$");
        if (!match.Success)
        {
            return null;
        }

        var prefix = match.Groups[1].Value;
        var pattern = $"{prefix}-*.apk";
        var files = Directory.GetFiles(".", pattern);
        if (files.Length == 0)
        {
            return null;
        }

        int maxCode = -1;
        string? maxFile = null;
        foreach (var file in files)
        {
            var nameOnly = Path.GetFileName(file);
            var code = ExtractVersionCodeFromFileName(nameOnly);
            if (code.HasValue && code.Value > maxCode)
            {
                maxCode = code.Value;
                maxFile = file;
            }
        }

        return maxFile;
    }

    // --- Resource helpers (engine-agnostic) ---
    public string BuildResourcePath(string sidOrName, ResourceType type, int diff)
    {
        return type switch
        {
            ResourceType.Illustration => $"Assets/Tracks/{sidOrName}/Illustration.jpg",
            ResourceType.IllustrationBlur => $"Assets/Tracks/{sidOrName}/IllustrationBlur.jpg",
            ResourceType.IllustrationLowRes => $"Assets/Tracks/{sidOrName}/IllustrationLowRes.jpg",
            ResourceType.Music => $"Assets/Tracks/{sidOrName}/music.wav",
            ResourceType.Chart => $"Assets/Tracks/{sidOrName}/Chart_{GetDiffStr(diff < 0 ? 2 : diff)}.json",
            ResourceType.CollectionAsset => sidOrName,
            ResourceType.Avatar => $"avatar.{sidOrName}",
            _ => throw new NotSupportedException($"Unsupported resource type: {type}")
        };
    }

    private string GetDiffStr(int diff) => diff switch { 0 => "EZ", 1 => "HD", 2 => "IN", 3 => "AT", _ => "IN" };

    public RawAssetResult GetRawAsset(string assetPath)
    {
        if (assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var data = GetAsset<UnityText>(assetPath);
            return RawAssetResult.FromJson(data.Content ?? string.Empty);
        }

        if (assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var music = PhiInfoDecoders.DecoderMusic(GetAsset<UnityMusic>(assetPath));
            return RawAssetResult.FromMusic(music ?? Array.Empty<byte>());
        }

        if (assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith("avatar.", StringComparison.OrdinalIgnoreCase) ||
            assetPath.Contains("Illustration", StringComparison.OrdinalIgnoreCase))
        {
            using var image = PhiInfoDecoders.DecoderImage(GetAsset<UnityImage>(assetPath));
            var normalized = image.CloneAs<Rgba32>();
            return RawAssetResult.FromImage(normalized);
        }

        throw new NotSupportedException($"Unsupported asset type: {assetPath}");
    }

    public sealed class RawAssetResult
    {
        public enum Kind { JsonString, MusicBytes, Image }
        public Kind Type { get; private set; }
        public string? Json { get; private set; }
        public byte[]? Music { get; private set; }
        public Image? Image { get; private set; }

        public static RawAssetResult FromJson(string json) => new RawAssetResult { Type = Kind.JsonString, Json = json };
        public static RawAssetResult FromMusic(byte[] music) => new RawAssetResult { Type = Kind.MusicBytes, Music = music };
        public static RawAssetResult FromImage(Image image) => new RawAssetResult { Type = Kind.Image, Image = image };
    }
}
