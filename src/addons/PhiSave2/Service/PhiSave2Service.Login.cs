using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhigrosLibraryCSharp.CloudSave;
using System.Collections.Generic;
using System.Linq;

// PhiSave2Service：云端登录、会话管理、用户信息获取等

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
    // ======初始化======
    /// <summary>
    /// 设置自定义云端服务器地址。若不设置则根据 ClientId 使用默认 TapTap 域名。
    /// </summary>
    /// <param name="server">云服务器根地址（可带或不带尾部斜杠）；传入 null 可清除自定义地址。</param>
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

    /// <summary>
    /// 使用会话 Token 与可选的 ClientId/ClientSecret 初始化云端 Save 客户端。
    /// </summary>
    /// <param name="token">LeanCloud/Phigros 的会话 Token，用于后续 API 调用。</param>
    /// <param name="clientId">可选的客户端 ID（覆盖默认值以使用自定义租户）。</param>
    /// <param name="clientSecret">可选的客户端密钥（覆盖默认值以使用自定义租户）。</param>
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
            // 不通过 RequestHandler 注入重定向，后续请求统一由显式 URL 构建逻辑处理。
        }
    }

    // ======二维码登录=======
    /// <summary>
    /// 二维码登录第一步：向 TapTap 请求登录二维码并在可用时触发 <see cref="QrCodeAvailable"/> 事件。
    /// </summary>
    /// <returns>返回包含二维码 URL 与过期信息的 <see cref="CompleteQRCodeData"/> 对象。</returns>
    public async Task<CompleteQRCodeData> RequestQrCodeAsync()
    {
        var qr = await TapTapHelper.RequestLoginQrCode();
        try { QrCodeAvailable?.Invoke(qr); } catch { }
        return qr;
    }

    /// <summary>
    /// 二维码登录第二步：查询二维码的轮询结果并触发 <see cref="QrCodeCheckResult"/> 事件。
    /// </summary>
    /// <param name="qr">来自 <see cref="RequestQrCodeAsync"/> 的二维码数据。</param>
    /// <returns>若用户已完成扫码并授权则返回 <see cref="TapTapTokenData"/>，否则返回 null 或未授权状态。</returns>
    public async Task<TapTapTokenData?> CheckQrCodeAsync(CompleteQRCodeData qr)
    {
        var res = await TapTapHelper.CheckQRCodeResult(qr);
        try { QrCodeCheckResult?.Invoke(res); } catch { }
        return res;
    }

    /// <summary>
    /// 二维码登录第三步：使用 TapTap 返回的数据获取用户资料并换取 Phigros/LeanCloud 会话 Token，随后初始化 Save 客户端。
    /// </summary>
    /// <param name="taptapData">来自 TapTap 的 token/授权数据。</param>
    /// <returns>返回获取到的会话 Token 字符串；若失败则可能返回空字符串。</returns>
    public async Task<string> CompleteLoginAsync(TapTapTokenData taptapData)
    {
        var profile = await TapTapHelper.GetProfile(taptapData.Data);
        var profileData = profile?.Data ?? throw new InvalidOperationException("TapTap profile data was empty.");
        _userObjectId = ExtractObjectId(profileData);
        var token = await LCHelper.LoginAndGetToken(new LCCombinedAuthData(profileData, taptapData.Data));
        Initialize(token, DefaultClientId, DefaultClientKey);
        // 如果配置文件未包含对象 ID，则尝试将 LeanCloud users/me 端点作为备用
        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }
        try { LoginCompleted?.Invoke(!string.IsNullOrWhiteSpace(token), string.IsNullOrWhiteSpace(token) ? "" : token); } catch { }
        return token;
    }

    // ====== OAuth 登录 ======
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

        // 生成 PKCE 参数
        var codeVerifier = GenerateRandomString(64);
        var codeChallenge = ComputePKCEChallenge(codeVerifier);

        var redirectUri = $"http://localhost:{port}/callback/";
        var url = authEndpoint + "?response_type=code" + "&client_id=" + Uri.EscapeDataString(clientId) +
                  "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&scope=" + Uri.EscapeDataString(scope) +
                  "&state=" + Uri.EscapeDataString(state) +
                  "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
                  "&code_challenge_method=S256"; // Removed &flow=pc_localhost

        // 开始监听器
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try { OAuthUrlGenerated?.Invoke(url); } catch { }

        // fire-and-forget 接受一个请求然后交换
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

                // 响应简单 HTML
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
                    // 用代码交换令牌
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
    
    // ======辅助方法：用户信息获取======
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
                Console.Error.WriteLine($"FetchUserObjectId failed: {resp.StatusCode} - {err}");
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

    private static string ExtractObjectId(object? profileData)
    {
        if (profileData == null) return string.Empty;
        // Try JsonElement
        if (profileData is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "objectId", "id", "userId", "openId", "uid" })
                {
                    if (je.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        // 尝试将对象的字符串表示解释为 JSON 并提取常见的 id 字段
        try
        {
            var s = profileData.ToString();
            if (!string.IsNullOrEmpty(s))
            {
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "objectId", "id", "userId", "openId", "uid" })
                    {
                        if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                            return v.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch { }

        return profileData.ToString() ?? string.Empty;
    }
}
