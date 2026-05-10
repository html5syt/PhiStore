using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;
using PhiStore.Testing;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 异步任务包装器，用于管理异步并向GDScript发送完成或错误信号
/// </summary>
[GlobalClass]
public partial class AsyncPhiSaveRequest : Godot.RefCounted
{
    [Signal]
    public delegate void CompletedEventHandler(Godot.Variant result);

    [Signal]
    public delegate void ErrorEventHandler(string message);

    public void Resolve(Godot.Variant result)
    {
        CallDeferred(MethodName.EmitSignal, SignalName.Completed, result);
    }

    public void Reject(string message)
    {
        CallDeferred(MethodName.EmitSignal, SignalName.Error, message);
    }
}

/// <summary>
/// PhiSave2 主 API 类，供 Godot 调用
/// </summary>
[GlobalClass]
public partial class PhiSave2API : RefCounted
{
    private readonly PhiSave2Service _service = new();
    private string _oldFileId = string.Empty;
    private string _oldObjId = string.Empty;

    public PhiSaveData? CurrentSave => _service.CurrentSave;

    /// <summary>
    /// GDScript 辅助方法：检查当前是否加载了存档到内存
    /// </summary>
    public bool HasCurrentSave() => _service.CurrentSave != null;

    [Signal]
    public delegate void QrCodeGeneratedEventHandler(string url, int expiresInSeconds);

    [Signal]
    public delegate void OAuthUrlGeneratedEventHandler(string url);

    [Signal]
    public delegate void OAuthLoginResultEventHandler(bool success, string sessionTokenOrError);

    [Signal]
    public delegate void LoginSuccessEventHandler(string sessionToken, string userObjectId);

    [Signal]
    public delegate void LoginFailedEventHandler(string errorMessage);

    public PhiSave2API()
    {
    }

    /// <summary>
    /// 请求二维码并开始轮询登录
    /// 返回的信号将包含二维码 URL，使用外部 UI 渲染
    /// </summary>
    public void StartQrLogin()
    {
        Task.Run(async () =>
        {
            try
            {
                var qrcode = await _service.RequestQrCodeAsync();
                CallDeferred(MethodName.EmitSignal, SignalName.QrCodeGenerated, qrcode.Url, qrcode.ExpiresInSeconds);

                TapTapTokenData? taptapData = null;
                while (true)
                {
                    taptapData = await _service.CheckQrCodeAsync(qrcode);
                    if (taptapData is not null) break;
                    await Task.Delay(3000);
                }

                var token = await _service.CompleteLoginAsync(taptapData);
                CallDeferred(MethodName.EmitSignal, SignalName.LoginSuccess, token, _service.UserObjectId);
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, ex.Message);
            }
        });
    }

    /// <summary>
    /// 使用已有的 Session Token 初始化
    /// </summary>
    public void InitWithSessionToken(string token)
    {
        _service.Initialize(token);
    }

    /// <summary>
    /// 使用 OAuth 授权码流程并在本地端口接收回调。
    /// 返回需要在浏览器中打开的授权 URL。登录成功/失败通过信号 `OAuthLoginResult` 返回。
    /// </summary>
    public void StartOAuthLogin(int port, string authEndpoint, string tokenEndpoint, string clientId, string clientSecret, string scope = "")
    {
        Task.Run(async () =>
        {
            try
            {
                var url = await _service.StartOAuthFlowAsync(port, authEndpoint, tokenEndpoint, clientId, clientSecret, scope);
                CallDeferred(MethodName.EmitSignal, SignalName.OAuthUrlGenerated, url);

                // wait for login result up to 60s
                var waited = 0;
                while (waited < 60)
                {
                    if (_service.IsLoggedIn)
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, true, _service.SessionToken);
                        return;
                    }
                    await Task.Delay(1000);
                    waited++;
                }
                CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, false, "timeout");
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, false, ex.Message);
            }
        });
    }

    /// <summary>
    /// 获取当前的 SessionToken 供持久化
    /// </summary>
    public string GetSessionToken() => _service.SessionToken;

    /// <summary>
    /// 下载云端存档并解包到内存
    /// </summary>
    public AsyncPhiSaveRequest DownloadAndDecryptSaveAsync()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var ids = await _service.DownloadSaveAsync();
                _oldFileId = ids.fileId;
                _oldObjId = ids.objId;

                request.Resolve(new Godot.Collections.Dictionary
                {
                    { "oldSaveGameFileObjectId", ids.fileId },
                    { "oldSaveObjectId", ids.objId }
                });
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 通过独立的 C# TapTap 解析器获取 APK 下载直链，用于 PhiInfo 的 Web 初始化。
    /// </summary>
    public AsyncPhiSaveRequest GetTapTapApkLinkAsync(int appId = 0)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var service = new TapTapApkLinkService();
                var info = await service.GetApkInfoAsync(appId > 0 ? appId : service.DefaultAppId);
                var result = new Godot.Collections.Dictionary
                {
                    { "download_url", info.DownloadUrl ?? string.Empty },
                    { "file_name", info.FileName ?? string.Empty },
                    { "version_code", info.VersionCode },
                    { "version_name", info.VersionName ?? string.Empty }
                };
                request.Resolve(result);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });

        return request;
    }

    /// <summary>
    /// 获取本地与云端存档的元信息用于冲突判断
    /// </summary>
    public AsyncPhiSaveRequest GetSaveMetadata(string? localFilePath)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var md = await _service.GetSaveMetadataAsync(localFilePath);
                var dict = new Godot.Collections.Dictionary
                {
                    { "local_modified_utc", md.LocalModifiedUtc?.ToString("o") ?? "" },
                    { "cloud_modified_utc", md.CloudModifiedUtc?.ToString("o") ?? "" },
                    { "local_rks", md.LocalRks },
                    { "cloud_rks", md.CloudRks }
                };
                request.Resolve(dict);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 获取本地(内存)与云端存档差异（主要song scores）
    /// </summary>
    public AsyncPhiSaveRequest DiffWithCloud()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var local = _service.CurrentSave;
                var (cloud, fileId, objId) = await _service.GetCloudSaveCopyAsync();
                var diffs = await _service.DiffSavesAsync(local, cloud);
                var arr = new Godot.Collections.Array();
                foreach (var d in diffs)
                {
                    var diffDict = new Godot.Collections.Dictionary();
                    diffDict["songId"] = d.SongId;
                    diffDict["difficulty"] = d.DifficultyIndex;
                    diffDict["local_score"] = d.LocalScore.HasValue ? d.LocalScore.Value : -1;
                    diffDict["local_acc"] = d.LocalAcc.HasValue ? d.LocalAcc.Value : -1f;
                    diffDict["cloud_score"] = d.CloudScore.HasValue ? d.CloudScore.Value : -1;
                    diffDict["cloud_acc"] = d.CloudAcc.HasValue ? d.CloudAcc.Value : -1f;
                    arr.Add(diffDict);
                }
                request.Resolve(arr);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 合并本地(内存)与云端存档，返回合并后的存档并将其设置为当前缓存（不自动上传）
    /// </summary>
    public AsyncPhiSaveRequest MergeWithCloud()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var local = _service.CurrentSave;
                var (cloud, fileId, objId) = await _service.GetCloudSaveCopyAsync();
                var merged = _service.MergeSaves(local, cloud);
                _service.CurrentSave = merged;
                request.Resolve(true);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 以本地为准上传（覆盖云端）
    /// </summary>
    public AsyncPhiSaveRequest KeepLocalAndUpload(string? oldFileId = null, string? oldObjId = null)
    {
        return UploadSaveAsync(oldFileId ?? _oldFileId, oldObjId ?? _oldObjId);
    }

    /// <summary>
    /// 以云端为准下载并覆盖本地缓存
    /// </summary>
    public AsyncPhiSaveRequest KeepCloudAndDownload()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var (cloud, fileId, objId) = await _service.GetCloudSaveCopyAsync();
                if (cloud == null) throw new Exception("No cloud save");
                _service.CurrentSave = cloud;
                request.Resolve(true);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 使用 AES+Gzip 将当前缓存(CurrentSave)持久化到本地 user:// 目录
    /// </summary>
    public string SaveToLocal(string path, byte[] key, byte[] iv)
    {
        if (_service.CurrentSave is null) return "No save in memory.";
        try
        {
            var globalPath = ProjectSettings.GlobalizePath(path);
            AESUtil.SaveEncrypted(globalPath, _service.CurrentSave, key, iv);
            return "OK";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 从本地文件加载加密的存档到当前内存缓存(CurrentSave)
    /// </summary>
    public string LoadFromLocal(string path, byte[] key, byte[] iv)
    {
        try
        {
            var globalPath = ProjectSettings.GlobalizePath(path);
            var loaded = AESUtil.LoadDecrypted(globalPath, key, iv);
            if (loaded != null)
            {
                _service.CurrentSave = loaded;
                return "OK";
            }
            return "File not found or format error.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 导出为 JSON 字符串
    /// </summary>
    public string ExportJson()
    {
        if (_service.CurrentSave is null) return "{}";
        return JsonSerializer.Serialize(_service.CurrentSave, PhiSaveJsonContext.Default.PhiSaveData);
    }

    /// <summary>
    /// 通过 JSON 字符串导入存档状态
    /// </summary>
    public string ImportJson(string jsonString)
    {
        try
        {
            var imported = (PhiSaveData?)JsonSerializer.Deserialize(jsonString, typeof(PhiSaveData), PhiSaveJsonContext.Default);
            if (imported != null)
            {
                _service.CurrentSave = imported;
                return "OK";
            }
            return "Parse error";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 基于内存中的存档进行RKS计算
    /// </summary>
    /// <param name="difficulties">字典: 歌曲ID_难度索引 -> Difficulty定数</param>
    public float CalculateInMemoryRks(Godot.Collections.Dictionary difficulties)
    {
        if (_service.CurrentSave?.Record?.Records is null) return 0f;

        var items = new List<DifficultyItem>();
        foreach (var songScoreEntry in _service.CurrentSave.Record.Records)
        {
            var songId = songScoreEntry.Id;
            var diffEnum = songScoreEntry.Difficulty;
            int diffIdx = (int)diffEnum;

            string searchKey = $"{songId}_{diffIdx}";
            if (difficulties.ContainsKey(searchKey))
            {
                float diffNum = (float)difficulties[searchKey];
                if (diffNum > 0f)
                {
                    items.Add(new DifficultyItem
                    {
                        SongId = songId,
                        Difficulty = diffNum,
                        Acc = songScoreEntry.Accuracy
                    });
                }
            }
        }
        float result = RKSCalculator.CalculateTotalRks(items);
        if (_service.CurrentSave != null)
        {
            _service.CurrentSave.SummaryRks = result;
        }
        return result;
    }

    /// <summary>
    /// 将内存数据重新包裹并上传到云端
    /// </summary>
    public AsyncPhiSaveRequest UploadSaveAsync(string? oldSaveGameFileObjectId = null, string? oldSaveObjectId = null)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                string fileId = oldSaveGameFileObjectId ?? _oldFileId;
                string objId = oldSaveObjectId ?? _oldObjId;

                await _service.UploadSaveAsync(fileId, objId);
                request.Resolve("OK");
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 辅助方法：生成用于本地AES加密的随机密钥
    /// </summary>
    public Godot.Collections.Array GenerateLocalKeys()
    {
        var keys = AESUtil.GenerateKeys();
        return new Godot.Collections.Array { keys.Key, keys.IV };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _service.Dispose();
        base.Dispose(disposing);
    }
}