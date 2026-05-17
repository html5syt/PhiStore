using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using Godot;
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
    public const string DefaultClientId = "rAK3FfdieFob2Nn8Am";
    public const string DefaultClientKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

    private string _sessionToken = string.Empty;
    private string _userObjectId = string.Empty;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string? _customCloudServer = null;
    private Save? _saveObj = null;

    public PhiSaveData? CurrentSave { get; set; }

    public string SessionToken => _sessionToken;
    public string UserObjectId => _userObjectId;

    public bool IsLoggedIn => _saveObj != null;

    /// <summary>
    /// 设置自定义云端服务器地址。如果不设置，将根据 ClientId 自动生成默认 TapTap 域名。
    /// </summary>
    public void SetCloudServer(string? server)
    {
        _customCloudServer = server?.TrimEnd('/');
    }

    private string GetCloudServerUrl(string clientId)
    {
        if (!string.IsNullOrEmpty(_customCloudServer)) return _customCloudServer;
        var cloudPrefix = clientId.Length >= 8 ? clientId.Substring(0, 8).ToLower() : clientId.ToLower();
        return $"https://{cloudPrefix}.cloud.tds1.tapapis.cn";
    }

    // ====== Conflict/metadata types ======
    public record SaveMetadata(DateTime? LocalModifiedUtc, DateTime? CloudModifiedUtc, float LocalRks, float CloudRks);

    public record ScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc);

    /// <summary>
    /// 初始化 Save 对象
    /// </summary>
    public void Initialize(string token, string clientId, string clientSecret)
    {
        _sessionToken = token;
        _clientId = clientId;
        _clientSecret = clientSecret;

        // 1. 根据 ClientId 长度和 customServer 判断是否为国际服
        bool isInternational = clientId.Length == 20;

        _saveObj = new Save(token, isInternational);

        // 2. 如果提供了自定义 ClientId/Secret，则修正默认 Header
        if (!string.IsNullOrEmpty(clientId))
        {
            _saveObj.Client.DefaultRequestHeaders.Remove("X-LC-Id");
            _saveObj.Client.DefaultRequestHeaders.Add("X-LC-Id", clientId);
        }
        if (!string.IsNullOrEmpty(clientSecret))
        {
            _saveObj.Client.DefaultRequestHeaders.Remove("X-LC-Key");
            _saveObj.Client.DefaultRequestHeaders.Add("X-LC-Key", clientSecret);
        }

        // 3. 如果设置了自定义服务器地址，使用 RequestHandler 进行重定向
        if (!string.IsNullOrEmpty(_customCloudServer))
        {
            var targetBase = _customCloudServer.TrimEnd('/');
            _saveObj.RequestHandler = async (s, req) =>
            {
                var original = req.RequestUri;
                if (original != null)
                {
                    var builder = new UriBuilder(targetBase)
                    {
                        Path = original.AbsolutePath,
                        Query = original.Query
                    };
                    req.RequestUri = builder.Uri;
                }
                return await s.Client.SendAsync(req);
            };
        }
    }

    private async Task<string> FetchUserObjectIdFromServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionToken)) return string.Empty;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("X-LC-Session", _sessionToken);

        if (!string.IsNullOrEmpty(_clientId)) client.DefaultRequestHeaders.Add("X-LC-Id", _clientId);
        if (!string.IsNullOrEmpty(_clientSecret)) client.DefaultRequestHeaders.Add("X-LC-Key", _clientSecret);

        try
        {
            var baseUrl = !string.IsNullOrEmpty(_customCloudServer) ? _customCloudServer : Save.CloudServerAddress;
            var url = baseUrl.TrimEnd('/') + "/1.1/users/me";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                GD.PrintErr($"FetchUserObjectId failed: {resp.StatusCode} - {err}");
                return string.Empty;
            }
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
        Initialize(token, DefaultClientId, DefaultClientKey);
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
        if (container.Results.Count == 0)
        {
            // 如果初步查询为空，尝试显式获取一次用户信息并重试。
            // 某些情况下，Session Token 需要激活或 User ID 需要明确加载。
            if (string.IsNullOrWhiteSpace(_userObjectId))
            {
                _userObjectId = await FetchUserObjectIdFromServerAsync();
            }
            container = await _saveObj.GetSaveInfoFromCloudAsync();
            if (container.Results.Count == 0)
            {
                throw new Exception("No cloud save found for this account. Ensure your server selection and game account match.");
            }
        }

        var targetIndex = -1;
        SaveContext? ctx = null;
        Exception? lastContextError = null;
        for (var i = 0; i < container.Results.Count; i++)
        {
            try
            {
                var candidateCtx = await _saveObj.GetSaveContextAsync(i);
                if (candidateCtx != null)
                {
                    targetIndex = i;
                    ctx = candidateCtx;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastContextError = ex;
            }
        }

        if (targetIndex < 0 || ctx == null)
        {
            var detail = lastContextError != null ? $" Last error: {lastContextError.Message}" : string.Empty;
            throw new Exception("Failed to load any cloud save context from server." + detail);
        }

        var info = container.Results[targetIndex];
        if (!string.IsNullOrWhiteSpace(info?.User?.ObjectId))
        {
            _userObjectId = info.User.ObjectId;
        }

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

        if (CurrentSave != null)
        {
            localTime = CurrentSave.ModifiedAt;
            localRks = CurrentSave.SummaryRks;
        }
        else if (!string.IsNullOrEmpty(localFilePath) && File.Exists(localFilePath))
        {
            localTime = File.GetLastWriteTimeUtc(localFilePath);
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
                cloudTime = await FetchCloudModifiedUtcFromRawAsync();
                try
                {
                    if (cloudTime == null)
                    {
                        var mod = info.ModifiedAt;
                        if (mod != null)
                        {
                            // Fallback approach: use ToString() and try to extract an ISO timestamp, or parse directly
                            var timestr = mod.ToString();
                            if (!string.IsNullOrEmpty(timestr))
                            {
                                try
                                {
                                    // Try to locate an "iso" field without regex to avoid escaping issues
                                    var isoKey = "\"iso\"";
                                    var idx = timestr.IndexOf(isoKey, StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        var colon = timestr.IndexOf(':', idx + isoKey.Length);
                                        if (colon >= 0)
                                        {
                                            var firstQuote = timestr.IndexOf('"', colon + 1);
                                            if (firstQuote >= 0)
                                            {
                                                var secondQuote = timestr.IndexOf('"', firstQuote + 1);
                                                if (secondQuote > firstQuote)
                                                {
                                                    var iso = timestr.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                                    if (DateTime.TryParse(iso, out var parsedF)) cloudTime = parsedF.ToUniversalTime();
                                                }
                                            }
                                        }
                                    }
                                    else if (DateTime.TryParse(timestr, out var parsedF2))
                                    {
                                        cloudTime = parsedF2.ToUniversalTime();
                                    }
                                }
                                catch { }
                            }
                        }
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
        if (profileData is IDictionary<string, object?> dict)
        {
            foreach (var key in new[] { "objectId", "ObjectId", "userObjectId", "UserObjectId" })
            {
                if (dict.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }
        else if (profileData is JsonElement elem || (profileData is string txt && TryParseJson(text: txt, out elem)))
        {
            if (elem.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "objectId", "ObjectId", "userObjectId", "UserObjectId" })
                {
                    if (elem.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                        return val.GetString() ?? string.Empty;
                }
            }
        }
        return string.Empty;
    }

    private static bool TryParseJson(string text, out JsonElement element)
    {
        element = default;
        try { element = JsonDocument.Parse(text).RootElement; return true; } catch { return false; }
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
    /// <summary>
    /// 开始 OAuth 流程。如果传入的 port 为 0，则自动选择一个可用端口。
    /// </summary>
    public async Task<string> StartOAuthFlowAsync(int port, string authEndpoint, string tokenEndpoint, string clientId, string clientSecret, string scope = "", string state = "phistore_state")
    {
        if (port <= 0)
        {
            var listenerForPort = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listenerForPort.Start();
            port = ((System.Net.IPEndPoint)listenerForPort.LocalEndpoint).Port;
            listenerForPort.Stop();
        }

        // Generate PKCE parameters
        var codeVerifier = GenerateRandomString(64);
        var codeChallenge = ComputePKCEChallenge(codeVerifier);

        var redirectUri = $"http://localhost:{port}/callback/";
        var url = authEndpoint + "?response_type=code" + "&client_id=" + Uri.EscapeDataString(clientId) +
                  "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&scope=" + Uri.EscapeDataString(scope) +
                  "&state=" + Uri.EscapeDataString(state) +
                  "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
                  "&code_challenge_method=S256"; // Removed &flow=pc_localhost

        // start listener
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(redirectUri);
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
                var error = q["error"];
                var returnedState = q["state"];

                // respond simple HTML
                var responseHtml = "<html><head><meta charset=\"UTF-8\"></head><body>已收到授权。您可以关闭此窗口。</body></html>";
                if (!string.IsNullOrEmpty(error))
                {
                    responseHtml = $"<html><head><meta charset=\"UTF-8\"></head><body>登录已取消或失败: {error}。您可以关闭此窗口。</body></html>";
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes(responseHtml);
                res.ContentType = "text/html";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                res.Close();

                if (!string.IsNullOrEmpty(error))
                {
                    throw new OperationCanceledException($"OAuth login canceled: {error}");
                }

                if (!string.IsNullOrEmpty(code))
                {
                    // exchange code for token
                    using var http = new System.Net.Http.HttpClient();
                    var dict = new Dictionary<string, string>
                    {
                        { "grant_type", "authorization_code" },
                        { "code", code },
                        { "client_id", clientId },
                        { "secret_type", "hmac-sha-1" },
                        { "redirect_uri", redirectUri },
                        { "code_verifier", codeVerifier }
                    };
                    var form = new System.Net.Http.FormUrlEncodedContent(dict);
                    var resp = await http.PostAsync(tokenEndpoint, form);

                    if (!resp.IsSuccessStatusCode)
                    {
                        var errorBody = await resp.Content.ReadAsStringAsync();
                        throw new Exception($"Token exchange failed: {resp.StatusCode} - {errorBody}");
                    }

                    var json = await resp.Content.ReadAsStringAsync();
                    try
                    {
                        var taptapData = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.TapTapTokenData);
                        if (taptapData?.Data == null) throw new Exception("Invalid TapTap token response.");

                        var profile = await TapTapHelper.GetProfile(taptapData.Data);
                        if (profile?.Data == null) throw new Exception("Failed to fetch TapTap profile for OAuth login.");

                        var sessionToken = await LCHelper.LoginAndGetToken(new LCCombinedAuthData(profile.Data, taptapData.Data));

                        if (!string.IsNullOrEmpty(sessionToken))
                        {
                            Initialize(sessionToken, DefaultClientId, DefaultClientKey);
                        }
                        else
                        {
                            throw new Exception("Failed to exchange TapTap token for LeanCloud session.");
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"OAuth token simplification error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OAuth exchange background task error: {ex}");
            }
            finally { try { listener.Stop(); } catch { } }
        });

        return url;
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private static string ComputePKCEChallenge(string verifier)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashed = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(verifier));
        var base64 = Convert.ToBase64String(hashed);
        return base64.Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    /// <summary>
    /// 上传存档到云端
    /// </summary>
    public async Task UploadSaveAsync(string? oldFileId, string? oldObjId)
    {
        if (_saveObj == null || CurrentSave == null) throw new InvalidOperationException("Missing state");

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(_sessionToken))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }

        // 获取原有的 SaveInfo 以构建上下文
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        var originalInfo = container.Results.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(originalInfo?.User?.ObjectId))
        {
            _userObjectId = originalInfo.User.ObjectId;
        }

        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            throw new InvalidOperationException("Missing user object id for upload.");
        }

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
        var targetIndex = -1;
        SaveContext? ctx = null;
        for (var i = 0; i < container.Results.Count; i++)
        {
            try
            {
                var candidateCtx = await _saveObj.GetSaveContextAsync(i);
                if (candidateCtx != null)
                {
                    targetIndex = i;
                    ctx = candidateCtx;
                    break;
                }
            }
            catch
            {
                // Skip broken save info entries.
            }
        }
        if (targetIndex < 0 || ctx == null) return (null, null, null);
        var info = container.Results[targetIndex];
        var copy = PhiSaveData.FromSaveContext(ctx);
        return (copy, info.GameFile?.ObjectId, info.ObjectId);
    }

    private async Task<DateTime?> FetchCloudModifiedUtcFromRawAsync()
    {
        if (_saveObj == null) return null;

        try
        {
            var baseUrl = !string.IsNullOrEmpty(_customCloudServer) ? _customCloudServer : Save.CloudServerAddress;
            var url = baseUrl.TrimEnd('/') + "/1.1/classes/_GameSave?limit=1";
            var resp = await _saveObj.Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var txt = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (!first.TryGetProperty("modifiedAt", out var modifiedAtElement)) return null;

            if (modifiedAtElement.ValueKind == JsonValueKind.String)
            {
                var s = modifiedAtElement.GetString();
                if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out var parsed)) return parsed.ToUniversalTime();
                return null;
            }

            if (modifiedAtElement.ValueKind == JsonValueKind.Object
                && modifiedAtElement.TryGetProperty("iso", out var isoElement)
                && isoElement.ValueKind == JsonValueKind.String)
            {
                var iso = isoElement.GetString();
                if (!string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, out var parsedIso)) return parsedIso.ToUniversalTime();
            }
        }
        catch
        {
            // Keep metadata retrieval best-effort.
        }

        return null;
    }
}
