using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhigrosLibraryCSharp.CloudSave;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
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
        try { LoginCompleted?.Invoke(!string.IsNullOrWhiteSpace(token), string.IsNullOrWhiteSpace(token) ? "" : token); } catch { }
        return token;
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

        // Try to interpret object's string representation as JSON and extract common id fields
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
