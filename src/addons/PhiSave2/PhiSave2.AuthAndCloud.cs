#nullable enable
using System;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp;
using PhigrosLibraryCSharp.Cloud.Login;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2
{
    /// <summary>
    /// 启动 TapTap 二维码登录流程。内部自动处理：
    /// 1) 请求二维码并通过 <see cref="LoginQrCodeReadyEventHandler"/> 通知调用方渲染二维码；
    /// 2) 轮询检查二维码扫码结果；
    /// 3) 获取用户 profile 并交换为 LeanCloud sessionToken；
    /// 4) 通过 <see cref="LoginCompletedEventHandler"/> 通知登录结果。
    /// 同时返回 <see cref="AsyncSaveRequest"/> 供额外监听。
    /// </summary>
    /// <param name="useChinaEndpoint">是否使用国内 TapTap/LeanCloud 地址。</param>
    /// <param name="permissions">请求的权限数组，通常为 ["public_profile"]。</param>
    /// <param name="pollIntervalMs">轮询二维码状态的间隔（毫秒）。</param>
    /// <returns>用于额外监听进度/完成的 <see cref="AsyncSaveRequest"/>。</returns>
    public AsyncSaveRequest StartQrLoginAsync(bool useChinaEndpoint = true, string[]? permissions = null, int pollIntervalMs = 3000)
    {
        var request = new AsyncSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.1f, "request_qr");
                CompleteQRCodeData qr = await TapTapHelper.RequestLoginQrCode(permissions, useChinaEndpoint);

                // 发射二维码信号给 GDScript 渲染
                CallDeferred(MethodName.EmitSignal, SignalName.LoginQrCodeReady, qr.Url, qr.ExpiresInSeconds, qr.DeviceID, qr.DeviceCode);

                // 内部轮询直到扫码成功或超时
                DateTime expiresAt = DateTime.UtcNow.AddSeconds(qr.ExpiresInSeconds);
                TapTapTokenData? tokenData = null;
                while (DateTime.UtcNow < expiresAt)
                {
                    await Task.Delay(pollIntervalMs);
                    tokenData = await TapTapHelper.CheckQRCodeResult(qr, useChinaEndpoint);
                    if (tokenData != null)
                        break;
                }

                if (tokenData == null)
                {
                    string errMsg = "QR login timed out.";
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, errMsg);
                    CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", errMsg);
                    return;
                }

                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.6f, "profile");
                TapTapProfileData profile = await TapTapHelper.GetProfile(tokenData.Data, 0, useChinaEndpoint);
                LCCombinedAuthData combined = new(profile.Data, tokenData.Data);
                string sessionToken = await LCHelper.LoginAndGetToken(combined, useChinaEndpoint, false);

                // 登录成功后写入内部 _save 并通知调用方
                string tapTapName = profile.Data.Name;
                string tapTapAvatar = profile.Data.Avatar;
                _save = new Save(sessionToken, !useChinaEndpoint);

                var result = new
                {
                    SessionToken = sessionToken,
                    TapTapName = tapTapName,
                    TapTapAvatar = tapTapAvatar
                };
                request.CallDeferred(AsyncSaveRequest.MethodName.SetResult, ToGodotVariant(result));
                CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, sessionToken, tapTapName, tapTapAvatar, "");
            }
            catch (Exception ex)
            {
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
                CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 启动 OAuth 登录流程。内部自动处理：
    /// 1) 在本地启动 HTTP 监听（默认端口 14514）；
    /// 2) 生成 TapTap 授权 URL 并通过 <see cref="OAuthLoginReadyEventHandler"/> 通知 GDScript 打开浏览器；
    /// 3) 等待浏览器回调本地端口获取授权 code；
    /// 4) 自动交换 token、获取 profile、设置会话；
    /// 5) 通过 <see cref="LoginCompletedEventHandler"/> 通知结果。
    /// 调用方无需任何额外操作，只需在收到 <c>OAuthLoginReady</c> 信号后调用 <c>OS.shell_open(beginUrl)</c> 即可。
    /// </summary>
    /// <param name="listenPort">本地监听端口，默认 14514。若被占用会自动尝试 +1。</param>
    /// <param name="useChinaEndpoint">是否使用国内端点。</param>
    /// <param name="permissions">请求的权限数组。</param>
    /// <returns>用于额外监听进度/完成的 <see cref="AsyncSaveRequest"/>。</returns>
    public AsyncSaveRequest StartOAuthLoginAsync(int listenPort = 14514, bool useChinaEndpoint = true, string[]? permissions = null)
    {
        var request = new AsyncSaveRequest();
        _pendingCallbackLogin = null;
        _oauthListening = false;
        _oauthListenPort = listenPort;

        Task.Run(async () =>
        {
            try
            {
                // 1. 启动本地 HTTP 监听
                string callbackUrl = $"http://127.0.0.1:{_oauthListenPort}/authorize";
                _oauthListener = new System.Net.HttpListener();
                _oauthListener.Prefixes.Add(callbackUrl + "/");
                try
                {
                    _oauthListener.Start();
                    _oauthListening = true;
                }
                catch (Exception)
                {
                    // 端口被占用，尝试 +1
                    _oauthListenPort = listenPort + 1;
                    callbackUrl = $"http://127.0.0.1:{_oauthListenPort}/authorize";
                    _oauthListener = new System.Net.HttpListener();
                    _oauthListener.Prefixes.Add(callbackUrl + "/");
                    _oauthListener.Start();
                    _oauthListening = true;
                }

                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.1f, "generate_url");

                // 2. 生成 TapTap 授权 URL
                _pendingCallbackLogin = TapTapHelper.GenerateCallbackLoginUrl(callbackUrl, useChinaEndpoint, permissions);

                // 3. 通知 GDScript 打开浏览器
                CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginReady, _pendingCallbackLogin.BeginUrl);

                // 4. 等待 HTTP 回调（异步非阻塞）
                var httpCtx = await _oauthListener.GetContextAsync();
                string? code = ExtractCodeFromOAuthRequest(httpCtx);
                httpCtx.Response.StatusCode = 302;

                if (!string.IsNullOrEmpty(code))
                {
                    // 成功：重定向到成功页面
                    httpCtx.Response.RedirectLocation = $"http://127.0.0.1:{_oauthListenPort}/authorize/success";
                }
                else
                {
                    httpCtx.Response.RedirectLocation = $"http://127.0.0.1:{_oauthListenPort}/authorize/error";
                }
                httpCtx.Response.Close();

                // 再返回一个简单页面告知用户可关闭浏览器
                _ = Task.Run(() => ServeCompletionPage());

                if (string.IsNullOrEmpty(code))
                {
                    string errMsg = "OAuth callback missing authorization code.";
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, errMsg);
                    CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", errMsg);
                    return;
                }

                // 5. 用 code 交换 token、获取 profile
                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.5f, "exchange_token");
                await CompleteOAuthFlowAsync(request, code, useChinaEndpoint);
            }
            catch (Exception ex)
            {
                CleanupOAuthListener();
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
                CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", ex.Message);
            }
        });

        return request;
    }

    /// <summary>
    /// 从 HTTP 回调请求中提取授权 code。
    /// </summary>
    private static string? ExtractCodeFromOAuthRequest(System.Net.HttpListenerContext ctx)
    {
        string? code = null;
        string? query = ctx.Request.Url?.Query;
        if (!string.IsNullOrEmpty(query))
        {
            var parsed = System.Web.HttpUtility.ParseQueryString(query);
            code = parsed["code"];
        }
        return code;
    }

    /// <summary>
    /// 提供授权完成后/出错时的告知页面，让用户知道可以关闭浏览器。
    /// </summary>
    private async Task ServeCompletionPage()
    {
        try
        {
            // 短暂延迟确保第一个响应已发送
            await Task.Delay(200);

            if (_oauthListener?.IsListening == true)
            {
                var ctx = await _oauthListener.GetContextAsync();
                string html = "<html><head><meta charset='utf-8'><title>授权完成</title>" +
                              "<style>body{font-family:sans-serif;display:flex;justify-content:center;" +
                              "align-items:center;height:100vh;margin:0;background:#f5f5f5}" +
                              ".card{background:white;padding:40px;border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,0.1);" +
                              "text-align:center}h1{color:#4CAF50}p{color:#666}}</style>" +
                              "</head><body><div class='card'>" +
                              "<h1>✓ 授权完成</h1><p>你可以安全地关闭此页面了。</p></div></body></html>";

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                ctx.Response.OutputStream.Close();
            }
        }
        catch
        {
            // 忽略——可能用户已关闭浏览器或监听已停止
        }
        finally
        {
            CleanupOAuthListener();
        }
    }

    /// <summary>
    /// 内部：用 code 完成 OAuth 交换（token + profile + session）。
    /// </summary>
    private async Task CompleteOAuthFlowAsync(AsyncSaveRequest request, string code, bool useChinaEndpoint)
    {
        try
        {
            if (_pendingCallbackLogin == null)
            {
                string errMsg = "OAuth login data missing.";
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, errMsg);
                CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", errMsg);
                return;
            }

            TapTapTokenData tokenData = await TapTapHelper.HandleCallbackLogin(_pendingCallbackLogin, code, useChinaEndpoint);

            request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.7f, "profile");
            TapTapProfileData profile = await TapTapHelper.GetProfile(tokenData.Data, 0, useChinaEndpoint);

            request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.9f, "session");
            LCCombinedAuthData combined = new(profile.Data, tokenData.Data);
            string sessionToken = await LCHelper.LoginAndGetToken(combined, useChinaEndpoint, false);

            string tapTapName = profile.Data.Name;
            string tapTapAvatar = profile.Data.Avatar;
            _save = new Save(sessionToken, !useChinaEndpoint);
            _pendingCallbackLogin = null;

            var result = new
            {
                SessionToken = sessionToken,
                TapTapName = tapTapName,
                TapTapAvatar = tapTapAvatar
            };

            request.CallDeferred(AsyncSaveRequest.MethodName.SetResult, ToGodotVariant(result));
            CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, sessionToken, tapTapName, tapTapAvatar, "");
        }
        catch (Exception ex)
        {
            CleanupOAuthListener();
            request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
            CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", ex.Message);
        }
    }

    /// <summary>
    /// 清理 OAuth HTTP 监听器。
    /// </summary>
    private void CleanupOAuthListener()
    {
        try
        {
            if (_oauthListener != null && _oauthListener.IsListening)
            {
                _oauthListener.Stop();
                _oauthListener.Close();
            }
        }
        catch
        {
            // 忽略
        }
        _oauthListener = null;
        _oauthListening = false;
    }

    /// <summary>
    /// 异步加载云端索引为 <paramref name="index"/> 的存档上下文，并缓存到内部状态。
    /// 返回的 <see cref="AsyncSaveRequest"/> 在完成时包含简要的元信息（Index、SummarySize、EntryCount）。
    /// </summary>
    /// <param name="index">云端存档索引。</param>
    /// <returns>用于监听加载结果的 <see cref="AsyncSaveRequest"/>。</returns>
    public AsyncSaveRequest LoadCloudSaveAsync(int index)
    {
        var request = new AsyncSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                if (_save == null)
                {
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, "Session not initialized.");
                    return;
                }

                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.2f, "download");
                var ctx = await _save.GetSaveContextAsync(index);
                CacheContext(ctx);

                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.6f, "metadata");
                _cloudEntry = await FetchCloudEntryAsync(index, _save);

                var result = new
                {
                    Index = index,
                    SummarySize = _rawSummary.Length,
                    EntryCount = _decryptedEntries.Count
                };
                request.CallDeferred(AsyncSaveRequest.MethodName.SetResult, ToGodotVariant(result));
            }
            catch (Exception ex)
            {
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 将当前缓存的解密后条目打包并上传到云端（分片/回调流程），返回用于监听的 <see cref="AsyncSaveRequest"/>。
    /// 确保先调用 <see cref="LoadCloudSaveAsync"/> 以获取云端元信息（_cloudEntry）。
    /// </summary>
    /// <returns>用于监听上传进度/完成的 <see cref="AsyncSaveRequest"/>。</returns>
    public AsyncSaveRequest UploadToCloudAsync()
    {
        var request = new AsyncSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                if (_save == null)
                {
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, "Session not initialized.");
                    return;
                }
                if (_cloudEntry == null)
                {
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, "Cloud save metadata missing. Load a save first.");
                    return;
                }

                byte[] zip = BuildZip();
                byte[] summary = _rawSummary.Length == 0 && !string.IsNullOrEmpty(_cloudEntry.SummaryBase64)
                    ? Convert.FromBase64String(_cloudEntry.SummaryBase64)
                    : _rawSummary;

                request.CallDeferred(AsyncSaveRequest.MethodName.SetProgress, 0.5f, "upload");
                await PhiSaveUploader.UploadSave(
                    _save,
                    _cloudEntry.UserObjectId,
                    _cloudEntry.GameFileObjectId,
                    _cloudEntry.SaveObjectId,
                    zip,
                    summary);
                request.CallDeferred(AsyncSaveRequest.MethodName.SetResult, Godot.Variant.CreateFrom(true));
            }
            catch (Exception ex)
            {
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
            }
        });
        return request;
    }
}
