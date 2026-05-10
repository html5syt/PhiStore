#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace PhiStore.Testing;

[GlobalClass]
public partial class TapTapApkLinkRequest : RefCounted
{
    [Signal]
    public delegate void CompletedEventHandler(string downloadUrl, string fileName, int versionCode, string versionName);

    [Signal]
    public delegate void ErrorEventHandler(string message);

    public void Resolve(string downloadUrl, string fileName, int versionCode, string versionName)
    {
        CallDeferred(MethodName.EmitSignal, SignalName.Completed, downloadUrl, fileName, versionCode, versionName);
    }

    public void Reject(string message)
    {
        CallDeferred(MethodName.EmitSignal, SignalName.Error, message);
    }
}

[GlobalClass]
public partial class TapTapApkLinkService : RefCounted
{
    private static readonly System.Net.Http.HttpClient Client = new System.Net.Http.HttpClient();
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

    public TapTapApkLinkRequest GetDownloadUrlAsync(int appId = 0)
    {
        var request = new TapTapApkLinkRequest();
        var resolvedAppId = appId > 0 ? appId : DefaultAppId;

        Task.Run(async () =>
        {
            try
            {
                var info = await GetApkInfoAsync(resolvedAppId);
                request.Resolve(info.DownloadUrl ?? string.Empty, info.FileName ?? string.Empty, info.VersionCode, info.VersionName ?? string.Empty);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });

        return request;
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
}