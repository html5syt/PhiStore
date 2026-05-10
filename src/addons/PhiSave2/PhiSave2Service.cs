using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Models;
using PhigrosLibraryCSharp.Serialization;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 纯 C# 实现的 PhiSave2 业务逻辑类。
/// 不依赖 Godot 引擎，专注于业务逻辑和性能。
/// </summary>
public class PhiSave2Service : IDisposable
{
    private string _sessionToken = string.Empty;
    private string _userObjectId = string.Empty;
    private Save? _saveObj = null;

    public PhiSaveData? CurrentSave { get; set; }

    public string SessionToken => _sessionToken;
    public string UserObjectId => _userObjectId;

    public bool IsLoggedIn => _saveObj != null;

    // ====== Conflict/metadata types ======
    public record SaveMetadata(DateTime? LocalModifiedUtc, DateTime? CloudModifiedUtc, float LocalRks, float CloudRks);

    public record ScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc);

    /// <summary>
    /// 初始化 Save 对象
    /// </summary>
    public void Initialize(string token)
    {
        _sessionToken = token;
        _saveObj = new Save(token, true);
    }

    private async Task<string> FetchUserObjectIdFromServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionToken)) return string.Empty;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("X-LC-Session", _sessionToken);

        try
        {
            var url = Save.CloudServerAddress.TrimEnd('/') + "/1.1/users/me";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return string.Empty;
            var txt = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("objectId", out var v) && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString() ?? string.Empty;
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>
    /// 二维码登录第一步：请求二维码
    /// </summary>
    public Task<CompleteQRCodeData> RequestQrCodeAsync() => TapTapHelper.RequestLoginQrCode();

    /// <summary>
    /// 二维码登录第二步：轮询结果
    /// </summary>
    public Task<TapTapTokenData?> CheckQrCodeAsync(CompleteQRCodeData qr) => TapTapHelper.CheckQRCodeResult(qr);

    /// <summary>
    /// 二维码登录第三步：换取 Phigros Token
    /// </summary>
    public async Task<string> CompleteLoginAsync(TapTapTokenData taptapData)
    {
        var profile = await TapTapHelper.GetProfile(taptapData.Data);
        var profileData = profile?.Data ?? throw new InvalidOperationException("TapTap profile data was empty.");
        _userObjectId = ExtractObjectId(profileData);
        var token = await LCHelper.LoginAndGetToken(new LCCombinedAuthData(profileData, taptapData.Data));
        Initialize(token);
        // If profile did not include an object id, try LeanCloud users/me endpoint as a fallback
        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }
        return token;
    }

    /// <summary>
    /// 同步云端存档
    /// </summary>
    public async Task<(string fileId, string objId)> DownloadSaveAsync()
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");
        // If we still don't have a user object id, try LeanCloud users/me as a best-effort fallback
        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(_sessionToken))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }

        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        if (container.Results.Count == 0) throw new Exception("No cloud save found");

        var info = container.Results[0];
        _userObjectId = info.User.ObjectId;

        var ctx = await _saveObj.GetSaveContextAsync(0);
        CurrentSave = PhiSaveData.FromSaveContext(ctx);
        // populate user object id from returned info if still missing
        try
        {
            if (string.IsNullOrWhiteSpace(_userObjectId) && info?.User?.ObjectId != null)
            {
                _userObjectId = info.User.ObjectId;
            }
        }
        catch { }

        string fileId = string.Empty;
        string objId = string.Empty;
        if (info != null)
        {
            if (info.GameFile != null && info.GameFile.ObjectId != null) fileId = info.GameFile.ObjectId;
            if (info.ObjectId != null) objId = info.ObjectId;
        }
        return (fileId, objId);
    }

    /// <summary>
    /// 获取本地与云端的存档元信息（修改时间与 Summary.Rks）
    /// localFilePath 可选：如果提供则读取其文件修改时间；否则使用内存 CurrentSave
    /// </summary>
    public async Task<SaveMetadata> GetSaveMetadataAsync(string? localFilePath = null)
    {
        DateTime? localTime = null;
        float localRks = 0f;
        if (!string.IsNullOrEmpty(localFilePath) && File.Exists(localFilePath))
        {
            localTime = File.GetLastWriteTimeUtc(localFilePath);
            // try to load and extract Rks if possible
            try
            {
                // attempt to decrypt using no key here is impossible; so prefer CurrentSave
                if (CurrentSave != null) localRks = CurrentSave.SummaryRks;
            }
            catch { }
        }
        else if (CurrentSave != null)
        {
            localRks = CurrentSave.SummaryRks;
        }

        DateTime? cloudTime = null;
        float cloudRks = 0f;
        if (_saveObj != null)
        {
            var container = await _saveObj.GetSaveInfoFromCloudAsync();
            if (container.Results.Count > 0)
            {
                var info = container.Results[0];
                try
                {
                    var mod = info.ModifiedAt;
                    if (mod != null)
                    {
                        // best-effort: use ToString() representation
                        string? timestr = mod.ToString();
                        if (!string.IsNullOrEmpty(timestr) && DateTime.TryParse(timestr, out var parsed)) cloudTime = parsed.ToUniversalTime();
                    }
                }
                catch { }
                try
                {
                    var sumField = info.Summary as string;
                    if (!string.IsNullOrEmpty(sumField))
                    {
                        var raw = Convert.FromBase64String(sumField);
                        var br = new ByteReader(raw);
                        var sum = Summary.FromReader(br);
                        cloudRks = sum.Rks;
                    }
                }
                catch { }
            }
        }

        return new SaveMetadata(localTime, cloudTime, localRks, cloudRks);
    }

    private static string ExtractObjectId(object? profileData)
    {
        if (profileData == null)
        {
            return string.Empty;
        }

        if (profileData is IDictionary<string, object?> dict)
        {
            if (TryGetDictionaryString(dict, "objectId", out var objectId) ||
                TryGetDictionaryString(dict, "ObjectId", out objectId) ||
                TryGetDictionaryString(dict, "userObjectId", out objectId) ||
                TryGetDictionaryString(dict, "UserObjectId", out objectId))
            {
                return objectId;
            }
        }

        if (profileData is JsonElement element)
        {
            foreach (var name in new[] { "objectId", "ObjectId", "userObjectId", "UserObjectId" })
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var extracted = value.GetString();
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        return extracted;
                    }
                }
            }
        }

        var text = profileData.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                foreach (var name in new[] { "objectId", "ObjectId", "userObjectId", "UserObjectId" })
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        var extracted = value.GetString();
                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            return extracted;
                        }
                    }
                }
            }
            catch { }
        }

        return string.Empty;
    }

    private static bool TryGetDictionaryString(IDictionary<string, object?> dict, string key, out string value)
    {
        value = string.Empty;
        if (!dict.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        if (raw is string str && !string.IsNullOrWhiteSpace(str))
        {
            value = str;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 计算本地与云端的差异（主要 song score 差异），用于 diff 展示
    /// </summary>
    public async Task<List<ScoreDiff>> DiffSavesAsync(PhiSaveData? local, PhiSaveData? cloud)
    {
        var diffs = new List<ScoreDiff>();
        if (local == null && cloud == null) return diffs;

        var mapLocal = new Dictionary<string, (int Score, float Acc)>();
        if (local?.Record?.Records != null)
        {
            foreach (var s in local.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mapLocal[key] = (s.Score, s.Accuracy);
            }
        }

        var mapCloud = new Dictionary<string, (int Score, float Acc)>();
        if (cloud?.Record?.Records != null)
        {
            foreach (var s in cloud.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mapCloud[key] = (s.Score, s.Accuracy);
            }
        }

        var keys = new HashSet<string>(mapLocal.Keys);
        keys.UnionWith(mapCloud.Keys);

        foreach (var k in keys)
        {
            mapLocal.TryGetValue(k, out var l);
            mapCloud.TryGetValue(k, out var c);
            if (l.Score != c.Score || Math.Abs(l.Acc - c.Acc) > 0.0001f)
            {
                var parts = k.Split('_');
                var songId = parts[0];
                var diffIdx = int.Parse(parts[1]);
                diffs.Add(new ScoreDiff(songId, diffIdx, mapLocal.ContainsKey(k) ? (int?)l.Score : null, mapLocal.ContainsKey(k) ? (float?)l.Acc : null, mapCloud.ContainsKey(k) ? (int?)c.Score : null, mapCloud.ContainsKey(k) ? (float?)c.Acc : null));
            }
        }

        return diffs;
    }

    /// <summary>
    /// 合并两个存档：按单曲分数取更高者，其他字段以最新非空为准
    /// </summary>
    public PhiSaveData MergeSaves(PhiSaveData? local, PhiSaveData? cloud)
    {
        var result = new PhiSaveData();

        // Merge Records
        var mergedRecords = new Dictionary<string, SongScore>();
        if (cloud?.Record?.Records != null)
        {
            foreach (var s in cloud.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mergedRecords[key] = s;
            }
        }
        if (local?.Record?.Records != null)
        {
            foreach (var s in local.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                if (mergedRecords.TryGetValue(key, out var orig))
                {
                    // choose the better one
                    if (s.Score > orig.Score || (s.Score == orig.Score && s.Accuracy > orig.Accuracy))
                        mergedRecords[key] = s;
                }
                else mergedRecords[key] = s;
            }
        }
        result.Record = new GameRecord(mergedRecords.Values.ToList(), 0);

        // For other sections prefer non-null and prefer local when scores equal
        result.Progress = local?.Progress ?? cloud?.Progress;
        result.UserInfo = local?.UserInfo ?? cloud?.UserInfo;
        result.Settings = local?.Settings ?? cloud?.Settings;

        // Merge Keys
        var keys = new Dictionary<string, GameKeyFlag>();
        if (cloud?.Keys != null)
            foreach (var kv in cloud.Keys) keys[kv.Key] = kv.Value;
        if (local?.Keys != null)
            foreach (var kv in local.Keys) keys[kv.Key] = kv.Value;
        result.Keys = keys;

        // Rks compute later
        result.SummaryRks = Math.Max(local?.SummaryRks ?? 0f, cloud?.SummaryRks ?? 0f);

        return result;
    }

    // ====== OAuth local callback support ======
    /// <summary>
    /// 启动本地 HTTP 回调监听并返回将要在浏览器打开的授权 URL。
    /// 调用方需在浏览器中打开返回的 URL。方法会在成功交换 token 后返回 session token。
    /// </summary>
    public async Task<string> StartOAuthFlowAsync(int port, string authEndpoint, string tokenEndpoint, string clientId, string clientSecret, string scope = "", string state = "phistore_state")
    {
        var redirectUri = $"http://localhost:{port}/callback/";
        var url = authEndpoint + "?response_type=code" + "&client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&scope=" + Uri.EscapeDataString(scope) + "&state=" + Uri.EscapeDataString(state);

        // start listener
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/callback/");
        listener.Start();

        // fire-and-forget accept one request then exchange
        _ = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                var req = ctx.Request;
                var res = ctx.Response;
                var q = req.QueryString;
                var code = q["code"];
                var returnedState = q["state"];

                // respond simple HTML
                var bytes = System.Text.Encoding.UTF8.GetBytes("<html><body>Received. You can close this window.</body></html>");
                res.ContentType = "text/html";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                res.Close();

                if (!string.IsNullOrEmpty(code))
                {
                    // exchange code for token
                    using var http = new System.Net.Http.HttpClient();
                    var form = new System.Net.Http.FormUrlEncodedContent(new[] {
                        new KeyValuePair<string,string>("grant_type","authorization_code"),
                        new KeyValuePair<string,string>("code", code),
                        new KeyValuePair<string,string>("client_id", clientId),
                        new KeyValuePair<string,string>("client_secret", clientSecret),
                        new KeyValuePair<string,string>("redirect_uri", redirectUri)
                    });
                    var resp = await http.PostAsync(tokenEndpoint, form);
                    resp.EnsureSuccessStatusCode();
                    var json = await resp.Content.ReadAsStringAsync();
                    // try to parse access_token
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("access_token", out var tok))
                        {
                            var token = tok.GetString() ?? string.Empty;
                            Initialize(token);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally { try { listener.Stop(); } catch { } }
        });

        return url;
    }

    /// <summary>
    /// 上传存档到云端
    /// </summary>
    public async Task UploadSaveAsync(string? oldFileId, string? oldObjId)
    {
        if (_saveObj == null || CurrentSave == null) throw new InvalidOperationException("Missing state");

        // 获取原有的 SaveInfo 以构建上下文
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        var originalInfo = container.Results.FirstOrDefault();

        var ctx = new SaveContext(new Dictionary<string, SaveContext.Entry>(), originalInfo!);
        CurrentSave.WriteToContext(ctx);

        byte[] packedSave;
        using (var ms = new MemoryStream())
        {
            // SaveContext may accept a ZipArchive + encryptor overload; try ZipArchive overload with encryptor that forwards to Save
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // pass encryptor that uses _saveObj.Encrypt if available
                if (_saveObj != null)
                {
                    await ctx.SaveToZipAsync(archive, async (b) => await _saveObj.Encrypt(b));
                }
                else
                {
                    await ctx.SaveToZipAsync(archive, async (b) => await Task.FromResult(b));
                }
            }
            packedSave = ms.ToArray();
        }

        // 构建 Summary
        // 这里需要计算准确的 Rks，调用方应在之前设置好 Rks 或在此计算
        // Update summary in ctx if possible then pack
        try
        {
            var existing = ctx.ReadSummary();
            if (existing != null)
            {
                existing.Rks = CurrentSave.SummaryRks;
                ctx.SaveSummary(existing);
            }
        }
        catch { }

        byte[] packedSummary = Array.Empty<byte>();
        try
        {
            var ctxSummary = ctx.ReadSummary();
            if (ctxSummary != null)
            {
                using var ms2 = new MemoryStream();
                var bw = new ByteWriter(ms2);
                ctxSummary.Serialize(bw);
                packedSummary = ms2.ToArray();
            }
        }
        catch { }

        await PhiSaveUploader.UploadSaveAsync(_saveObj!, _userObjectId, oldFileId, oldObjId, packedSave, packedSummary);
    }

    public void Dispose()
    {
        _saveObj?.Dispose();
    }

    /// <summary>
    /// 获取云端存档的副本而不覆盖 CurrentSave
    /// 返回 (PhiSaveData, fileId, objId)
    /// </summary>
    public async Task<(PhiSaveData? CloudSave, string? FileId, string? ObjId)> GetCloudSaveCopyAsync()
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        if (container.Results.Count == 0) return (null, null, null);
        var info = container.Results[0];
        var ctx = await _saveObj.GetSaveContextAsync(0);
        var copy = PhiSaveData.FromSaveContext(ctx);
        return (copy, info.GameFile?.ObjectId, info.ObjectId);
    }
}
