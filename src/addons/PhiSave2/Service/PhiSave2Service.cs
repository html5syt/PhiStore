using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Models;
using PhiStore.Addons.PhiSave2.Internal;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 纯 C# 实现的 PhiSave2 业务逻辑类。
/// 不依赖 Godot 引擎，专注于业务逻辑和性能。
/// </summary>
public partial class PhiSave2Service : IDisposable
{
    public const string DefaultClientId = "rAK3FfdieFob2Nn8Am";
    public const string DefaultClientKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

    private string _sessionToken = string.Empty;
    private string _userObjectId = string.Empty;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string? _customCloudServer = null;
    private Save? _saveObj = null;
    // transient preview merge storage
    private PhiSaveData? _previewMerge = null;

    // Events for UI/host to subscribe to when using service without Godot
    public event Action<CompleteQRCodeData>? QrCodeAvailable;
    public event Action<TapTapTokenData?>? QrCodeCheckResult;
    public event Action<string>? OAuthUrlGenerated;
    /// <summary>
    /// (success, sessionTokenOrError)
    /// </summary>
    public event Action<bool, string>? LoginCompleted;

    public PhiSaveData? CurrentSave { get; set; }

    public string SessionToken => _sessionToken;
    public string UserObjectId => _userObjectId;

    public bool IsLoggedIn => _saveObj != null;

    // ====== Conflict/metadata types ======
    public record SaveMetadata(DateTime? LocalModifiedUtc, DateTime? CloudModifiedUtc, float LocalRks, float CloudRks);

    public record ScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc);

    public record DetailedScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc, string Suggestion);

    public record DetailedScoreDiffWithObjects(DetailedScoreDiff Diff, SongScore? LocalObj, SongScore? CloudObj);

    public record SyncResult(string Status, string Message, List<ScoreDiff>? Diffs = null, string? FileId = null, string? ObjId = null);

    // Full-diff item for non-score parts of the save
    public record FullDiff(string Path, string DiffType, string? LocalValue, string? CloudValue);

    // ===== RKS calculation details =====
    public record RksEntry(string SongId, int DifficultyIndex, string DifficultyName, float DifficultyValue, float Acc, float Rks, bool IsPhi);
    public record RksDetails(List<RksEntry> Entries, float SumB27, float SumTop3Phi, float TotalRks);

    private static bool TryResolveDifficultyLocal(Dictionary<string, float> difficulties, string songId, int diffIdx, string diffName, out float diffNum)
    {
        diffNum = 0f;
        if (difficulties == null) return false;
        var key = $"{songId}_{diffIdx}";
        if (difficulties.TryGetValue(key, out diffNum)) return true;

        if (!string.IsNullOrWhiteSpace(diffName))
        {
            key = $"{songId}_{diffName}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
            var upper = diffName.ToUpperInvariant();
            key = $"{songId}_{upper}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
        }

        string? alias = diffIdx switch
        {
            0 => "EZ",
            1 => "HD",
            2 => "IN",
            3 => "AT",
            4 => "LEGACY",
            _ => null
        };

        if (!string.IsNullOrEmpty(alias))
        {
            key = $"{songId}_{alias}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
        }

        return false;
    }

    /// <summary>
    /// 为当前 `CurrentSave` 中已存在的成绩项计算每首歌的 RKS 条目以及总体汇总值。
    /// difficulties 参数为歌曲难度映射（键格式："{songId}_{difficultyNameOrIndex}" -> 数值难度）。
    /// 返回包含逐项条目与聚合指标的 <see cref="RksDetails"/>。
    /// </summary>
    /// <param name="difficulties">映射歌曲难度的字典，用于计算单首曲目的 RKS。</param>
    /// <returns>包含条目列表及汇总 RKS 值的 <see cref="RksDetails"/> 对象。</returns>
    public RksDetails CalculateRksDetails(Dictionary<string, float> difficulties)
    {
        var entries = new List<RksEntry>();
        if (CurrentSave?.Record?.Records == null) return new RksDetails(entries, 0f, 0f, 0f);

        var items = new List<DifficultyItem>();

        foreach (var s in CurrentSave.Record.Records)
        {
            var songId = s.Id;
            var diffEnum = s.Difficulty;
            int diffIdx = (int)diffEnum;
            var diffName = diffEnum.ToString();
            if (TryResolveDifficultyLocal(difficulties, songId, diffIdx, diffName, out var diffVal) && diffVal > 0f)
            {
                items.Add(new DifficultyItem { SongId = songId, Difficulty = diffVal, Acc = s.Accuracy });
                var rks = RKSCalculator.CalculateSingleRks(s.Accuracy, diffVal);
                entries.Add(new RksEntry(songId, diffIdx, diffName, diffVal, s.Accuracy, rks, s.Accuracy >= 100f));
            }
        }

        // compute aggregated metrics using same algorithm
        var allCalculated = items
            .Select(i => new { Item = i, Rks = RKSCalculator.CalculateSingleRks(i.Acc, i.Difficulty), IsPhi = i.Acc >= 100f })
            .Where(x => x.Rks > 0f)
            .OrderByDescending(x => x.Rks)
            .ToList();

        var sumB27 = allCalculated.Take(27).Sum(x => x.Rks);
        var sumTop3Phi = allCalculated.Where(x => x.IsPhi).Take(3).Sum(x => x.Rks);
        var total = (float)Math.Round((sumB27 + sumTop3Phi) / 30f, 4);

        return new RksDetails(entries, (float)Math.Round(sumB27, 4), (float)Math.Round(sumTop3Phi, 4), total);
    }

    /// <summary>
    /// 根据传入的 difficulty map 计算 RKS 并应用到 CurrentSave（同时更新 GameSummary.Rks 如果存在）。
    /// 返回计算得到的 total RKS 值。
    /// </summary>
    public float ComputeAndApplyInMemoryRks(Dictionary<string, float> difficulties)
    {
        var details = CalculateRksDetails(difficulties);
        if (CurrentSave == null) CurrentSave = new PhiSaveData();
        CurrentSave.SummaryRks = details.TotalRks;
        if (CurrentSave.GameSummary != null)
        {
            CurrentSave.GameSummary.Rks = details.TotalRks;
        }
        return details.TotalRks;
    }

    // ====== OAuth local callback support ======
    /// <summary>
    /// 启动本地 HTTP 回调监听并返回将要在浏览器打开的授权 URL。
    /// 调用方需在浏览器中打开返回的 URL。方法会在成功交换 token 后通过 <see cref="LoginCompleted"/> 事件通知结果。
    /// 如果传入的 <paramref name="port"/> 为 0，则自动选择一个可用端口。
    /// </summary>
    /// <param name="port">本地回调监听端口，传 0 自动选择可用端口。</param>
    /// <param name="authEndpoint">OAuth 授权端点 URL。</param>
    /// <param name="tokenEndpoint">OAuth Token 交换端点 URL。</param>
    /// <param name="clientId">客户端 ID。</param>
    /// <param name="clientSecret">客户端密钥。</param>
    /// <param name="scope">请求的权限范围（可选）。</param>
    /// <param name="state">可选的状态参数，用于防止 CSRF。</param>
    /// <returns>返回用于在浏览器中打开的授权 URL 字符串。</returns>
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

        try { OAuthUrlGenerated?.Invoke(url); } catch { }

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
                            try { LoginCompleted?.Invoke(true, sessionToken); } catch { }
                        }
                        else
                        {
                            try { LoginCompleted?.Invoke(false, "Failed to exchange TapTap token for LeanCloud session."); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"OAuth token simplification error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"OAuth exchange background task error: {ex}");
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

    // Storage- and upload-related methods have been moved to
    // PhiSave2Service.Storage.cs to keep this file focused on core logic.
}
