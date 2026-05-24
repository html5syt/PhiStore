using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhigrosLibraryCSharp.CloudSave;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;
using PhigrosLibraryCSharp.Serialization;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
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
    /// 二维码登录第一步：请求二维码
    /// </summary>
    public async Task<CompleteQRCodeData> RequestQrCodeAsync()
    {
        var qr = await TapTapHelper.RequestLoginQrCode();
        try { QrCodeAvailable?.Invoke(qr); } catch { }
        return qr;
    }

    /// <summary>
    /// 二维码登录第二步：轮询结果
    /// </summary>
    public async Task<TapTapTokenData?> CheckQrCodeAsync(CompleteQRCodeData qr)
    {
        var res = await TapTapHelper.CheckQRCodeResult(qr);
        try { QrCodeCheckResult?.Invoke(res); } catch { }
        return res;
    }

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
        try { LoginCompleted?.Invoke(!string.IsNullOrWhiteSpace(token), string.IsNullOrWhiteSpace(token) ? "" : token); } catch { }
        return token;
    }

    private static string ExtractObjectId(object? profileData)
    {
        if (profileData == null) return string.Empty;
        // Try JsonElement
        if (profileData is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var name in new[] { "objectId", "id", "userId", "openId", "uid" })
                {
                    if (je.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
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
