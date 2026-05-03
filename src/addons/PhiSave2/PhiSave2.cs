#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PhigrosLibraryCSharp;
using PhigrosLibraryCSharp.Cloud.Login;

namespace PhiStore.Addons.PhiSave2;

[GlobalClass]
/// <summary>
/// 主入口类 `PhiSave2`：负责会话管理、存档缓存与基础读写/打包接口。
/// 将多个功能拆分为 partial 文件以便维护（Auth/Cloud、Data/RKS、Internal 等）。
/// </summary>
public partial class PhiSave2 : RefCounted
{
    public enum Difficulty : int
    {
        EZ = 0,
        HD = 1,
        IN = 2,
        AT = 3,
        Legacy = 4
    }

    [Signal]
    public delegate void LoginQrCodeReadyEventHandler(string url, int expiresInSeconds, string deviceId, string deviceCode);

	/// <summary>
	/// OAuth 登录流程准备完成，浏览器可以打开时触发。
	/// GDScript 端收到此信号后可调用 <c>OS.shell_open(beginUrl)</c> 打开浏览器。
	/// </summary>
	/// <param name="beginUrl">用户应在浏览器中打开的授权 URL。</param>
	[Signal]
	public delegate void OAuthLoginReadyEventHandler(string beginUrl);

	/// <summary>
	/// 任意登录流程（QR/OAuth/SessionToken）完成时触发。
	/// 成功时 errorMessage 为空字符串，失败时 sessionToken/tapTapName/tapTapAvatar 为空。
	/// </summary>
	[Signal]
	public delegate void LoginCompletedEventHandler(string sessionToken, string tapTapName, string tapTapAvatar, string errorMessage);

    private const string CloudAesKey = "6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks=";
    private const string CloudAesIv = "Kk/wisgNYwcAV8WVGMgyUw==";

    private static readonly byte[] AesKey = Convert.FromBase64String(CloudAesKey);
    private static readonly byte[] AesIv = Convert.FromBase64String(CloudAesIv);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        IncludeFields = true
    };

    private Save? _save;
    private SaveContext? _context;
    private CallbackLoginData? _pendingCallbackLogin;

    private byte[] _rawZip = Array.Empty<byte>();
    private byte[] _rawSummary = Array.Empty<byte>();
    private readonly Dictionary<string, byte[]> _rawEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _decryptedEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte> _entryHeaders = new(StringComparer.Ordinal);

    private CloudSaveEntry? _cloudEntry;

    // OAuth HTTP 本地回调监听
    private System.Net.HttpListener? _oauthListener;
    private int _oauthListenPort = 14514;
    private bool _oauthListening;

    private sealed class CloudSaveEntry
    {
        public required string UserObjectId { get; init; }
        public required string SaveObjectId { get; init; }
        public string? GameFileObjectId { get; init; }
        public required string GameFileUrl { get; init; }
        public required string SummaryBase64 { get; init; }
    }

    public bool HasLoadedSave => _decryptedEntries.Count > 0;

    public void ResetState()
    {
        _context = null;
        _rawZip = Array.Empty<byte>();
        _rawSummary = Array.Empty<byte>();
        _rawEntries.Clear();
        _decryptedEntries.Clear();
        _entryHeaders.Clear();
        _cloudEntry = null;
        _pendingCallbackLogin = null;
        CleanupOAuthListener();
    }


    /// <summary>
    /// 使用已有 sessionToken 初始化会话（设置 <see cref="Save"/> 实例），
    /// 并发射 <see cref="LoginCompletedEventHandler"/> 信号通知调用方。
    /// </summary>
    /// <param name="sessionToken">从 TapTap/LC 获取的 session token。</param>
    /// <param name="tapTapName">可选：TapTap 用户名（留空则使用 token 前缀）。</param>
    /// <param name="isInternational">是否使用国际服地址。</param>
    public void LoginWithSessionToken(string sessionToken, string tapTapName = "", bool isInternational = false)
    {
        _save = new Save(sessionToken, isInternational);
        EmitSignal(SignalName.LoginCompleted,
            sessionToken,
            string.IsNullOrEmpty(tapTapName) ? sessionToken[..Math.Min(8, sessionToken.Length)] : tapTapName,
            "",
            "");
    }

    /// <summary>
    /// 检查当前是否存在已初始化的会话。
    /// </summary>
    /// <returns>若已初始化返回 true。</returns>
    public bool HasSession() => _save != null;

    /// <summary>
    /// 从本地 ZIP 文件加载存档数据（解密并缓存到实例字段）。可选地提供 summary 的 Base64 字符串。
    /// </summary>
    /// <param name="zipPath">ZIP 文件路径（支持 Godot 的资源路径，如 <c>user://save.zip</c>）。</param>
    /// <param name="summaryBase64">可选的 summary 字符串（Base64）。</param>
    public void LoadLocalZip(string zipPath, string? summaryBase64 = null)
    {
        string fullPath = ProjectSettings.GlobalizePath(zipPath);
        byte[] zipData = File.ReadAllBytes(fullPath);
        LoadZipBytes(zipData, summaryBase64);
    }


    /// <summary>
    /// 以字节数据加载 ZIP（可用于从网络或内存直接加载）。内部会解密每个条目并缓存到实例的字典中。
    /// </summary>
    /// <param name="zipData">ZIP 的原始字节数组。</param>
    /// <param name="summaryBase64">可选的 summary 字符串（Base64）。</param>
    public void LoadZipBytes(byte[] zipData, string? summaryBase64 = null)
    {
        ResetState();
        _rawZip = zipData;
        _rawSummary = string.IsNullOrEmpty(summaryBase64) ? Array.Empty<byte>() : Convert.FromBase64String(summaryBase64);

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zipData), System.IO.Compression.ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            byte[] raw = new byte[entry.Length];
            stream.ReadExactly(raw);
            _rawEntries[entry.Name] = raw;
            _entryHeaders[entry.Name] = raw.Length > 0 ? raw[0] : (byte)0;
            _decryptedEntries[entry.Name] = DecryptLocal(raw.Length > 1 ? raw[1..] : Array.Empty<byte>());
        }
    }

    /// <summary>
    /// 获取当前加载的 ZIP 原始字节数据。若未加载任何存档则返回空数组。
    /// </summary>
    /// <returns>ZIP 文件的原始字节数组。</returns>
	public byte[] GetRawZipBytes()
    {
        return _rawZip;
    }

    /// <summary>
    /// 获取当前缓存的 summary 二进制数据（原始字节）。若未设置则返回空数组。
    /// </summary>
    /// <returns>summary 的原始字节数组。</returns>
	public byte[] GetSummaryBinary()
    {
        return _rawSummary;
    }

    /// <summary>
    /// 替换当前缓存的 summary 二进制数据。修改后会反映到后续打包与上传操作中。
    /// </summary>
    /// <param name="data">新的 summary 字节数组。</param>
	public void SetSummaryBinary(byte[] data)
    {
        _rawSummary = data;
    }

    /// <summary>
    /// 根据条目名称获取解密后的条目数据。若该条目不存在则返回空数组。
    /// </summary>
    /// <param name="entryName">条目名称（如 "gameRecord"、"settings"、"gameProgress"、"user"）。</param>
    /// <returns>解密后的条目字节数组，不存在时返回空数组。</returns>
	public byte[] GetDecryptedEntry(string entryName)
    {
        return _decryptedEntries.TryGetValue(entryName, out byte[]? data) ? data : Array.Empty<byte>();
    }

    /// <summary>
    /// 设置或替换指定条目的解密数据。修改后会在下次 <see cref="PackToZipBytes"/> 或
    /// <see cref="SaveToLocalZip"/> 时被重新加密并打包。
    /// </summary>
    /// <param name="entryName">条目名称。</param>
    /// <param name="data">新的解密后字节数组。</param>
	public void SetDecryptedEntry(string entryName, byte[] data)
    {
        _decryptedEntries[entryName] = data;
    }

    /// <summary>
    /// 将当前缓存的所有解密条目重新加密并打包为 ZIP 字节数组。
    /// 可用于导出、上传或进一步处理。
    /// </summary>
    /// <returns>包含所有条目的 ZIP 字节数组。</returns>
	public byte[] PackToZipBytes()
    {
        return BuildZip();
    }

    /// <summary>
    /// 将当前缓存的所有解密条目重新加密、打包为 ZIP 并写入本地文件。
    /// 路径支持 Godot 资源路径（如 <c>user://save.zip</c>），会自动转换为系统绝对路径。
    /// </summary>
    /// <param name="zipPath">输出 ZIP 文件路径（支持 Godot 资源路径）。</param>
	public void SaveToLocalZip(string zipPath)
    {
        byte[] zip = BuildZip();
        string fullPath = ProjectSettings.GlobalizePath(zipPath);
        File.WriteAllBytes(fullPath, zip);
    }
}
