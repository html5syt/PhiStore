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
    /// 开始 OAuth 回调登录流程。内部处理：
    /// 1) 生成回调登录 URL/State 信息并返回（调用方可据此打开浏览器）；
    /// 2) 调用方在获得回调 code 后调用 <see cref="CompleteOAuthLoginAsync"/> 即可完成登录。
    /// </summary>
    /// <param name="callbackUrl">回调地址（通常为本地监听地址，如 <c>http://127.0.0.1:14514/authorize</c>）。</param>
    /// <param name="useChinaEndpoint">是否使用国内端点。</param>
    /// <param name="permissions">请求的权限数组。</param>
    /// <returns>包含回调登录所需信息（BeginUrl、RedirectUrl、State、Scope）的 Godot Dictionary。</returns>
    public Godot.Variant StartOAuthLogin(string callbackUrl, bool useChinaEndpoint = true, string[]? permissions = null)
    {
        _pendingCallbackLogin = TapTapHelper.GenerateCallbackLoginUrl(callbackUrl, useChinaEndpoint, permissions);
        var payload = new
        {
            _pendingCallbackLogin.BeginUrl,
            _pendingCallbackLogin.RedirectUrl,
            _pendingCallbackLogin.State,
            _pendingCallbackLogin.Scope
        };
        return ToGodotVariant(payload);
    }

    /// <summary>
    /// 使用 OAuth 回调返回的 code 完成登录。内部自动交换 token、获取 profile、
    /// 设置会话并通过 <see cref="LoginCompletedEventHandler"/> 通知结果。
    /// 同时返回 <see cref="AsyncSaveRequest"/> 供额外监听。
    /// </summary>
    /// <param name="code">回调返回的授权 code。</param>
    /// <param name="useChinaEndpoint">是否使用国内端点。</param>
    /// <returns>用于额外监听进度/完成的 <see cref="AsyncSaveRequest"/>。</returns>
    public AsyncSaveRequest CompleteOAuthLoginAsync(string code, bool useChinaEndpoint = true)
    {
        var request = new AsyncSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                if (_pendingCallbackLogin == null)
                {
                    string errMsg = "OAuth login data missing. Call StartOAuthLogin first.";
                    request.CallDeferred(AsyncSaveRequest.MethodName.SetError, errMsg);
                    CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", errMsg);
                    return;
                }

                TapTapTokenData tokenData = await TapTapHelper.HandleCallbackLogin(_pendingCallbackLogin, code, useChinaEndpoint);
                TapTapProfileData profile = await TapTapHelper.GetProfile(tokenData.Data, 0, useChinaEndpoint);
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
                request.CallDeferred(AsyncSaveRequest.MethodName.SetError, ex.Message);
                CallDeferred(MethodName.EmitSignal, SignalName.LoginCompleted, "", "", "", ex.Message);
            }
        });
        return request;
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
