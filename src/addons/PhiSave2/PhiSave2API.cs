using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;

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
    /// 设置自定义云端服务器地址。
    /// </summary>
    public void SetCloudServer(string server) => _service.SetCloudServer(server);

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
    public void InitWithSessionToken(string token, string clientId = "", string clientSecret = "")
    {
        // Fallback to defaults if not provided to support Phigros cloud
        if (string.IsNullOrEmpty(clientId)) clientId = PhiSave2Service.DefaultClientId;
        if (string.IsNullOrEmpty(clientSecret)) clientSecret = PhiSave2Service.DefaultClientKey;
        _service.Initialize(token, clientId, clientSecret);
    }

    /// <summary>
    /// 使用 OAuth 授权码流程并在本地端口接收回调。
    /// 简化版本：现在支持默认参数运行，无需强制传入凭据。
    /// 登录成功/失败通过信号 `OAuthLoginResult` 返回。
    /// </summary>
    public void StartOAuthLogin(int port = 0, string? authEndpoint = null, string? tokenEndpoint = null, string? clientId = null, string? clientSecret = null, string scope = "")
    {
        Task.Run(async () =>
        {
            try
            {
                var target_auth = authEndpoint ?? "https://accounts.taptap.com/authorize";
                var target_token = tokenEndpoint ?? "https://accounts.tapapis.cn/oauth2/v1/token";
                var target_id = clientId ?? PhiSave2Service.DefaultClientId;
                var target_secret = clientSecret ?? PhiSave2Service.DefaultClientKey;

                var url = await _service.StartOAuthFlowAsync(port, target_auth, target_token, target_id, target_secret, scope);
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

                var result = new Godot.Collections.Dictionary();
                result["score_diffs"] = arr;

                result["local_summary"] = SerializeSummaryToGD(local?.GameSummary);
                result["cloud_summary"] = SerializeSummaryToGD(cloud?.GameSummary);

                request.Resolve(result);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    private Godot.Collections.Dictionary SerializeSummaryToGD(PhigrosLibraryCSharp.CloudSave.Summary? sum)
    {
        var dict = new Godot.Collections.Dictionary();
        if (sum == null) return dict;
        dict["gameVersion"] = sum.GameVersion;
        dict["rks"] = sum.Rks;
        dict["avatar"] = sum.Avatar ?? "";
        dict["challenge"] = (int)sum.Challenge.RawCode;
        return dict;
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
    /// 将内存中的 PhiSaveData 保存到本地加密文件。
    /// 支持传入自定义密钥和 IV 供调试。
    /// </summary>
    public string SaveToLocal(string path, byte[]? key = null, byte[]? iv = null)
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
    /// 支持传入自定义密钥和 IV 供调试。
    /// </summary>
    public string LoadFromLocal(string path, byte[]? key = null, byte[]? iv = null)
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
        return PhiSaveJsonCompat.Serialize(_service.CurrentSave);
    }

    /// <summary>
    /// 导出 JSON 存档到文件
    /// </summary>
    public string ExportJsonToFile(string path)
    {
        try
        {
            var json = ExportJson();
            if (json == "{}") return "No save in memory";
            var globalPath = ProjectSettings.GlobalizePath(path);
            System.IO.File.WriteAllText(globalPath, json);
            return "OK";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 通过 JSON 字符串导入存档状态
    /// </summary>
    public string ImportJson(string jsonString)
    {
        try
        {
            var imported = PhiSaveJsonCompat.Deserialize(jsonString);
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
    /// 从 JSON 明文文件导入存档状态
    /// </summary>
    public string ImportJsonFromFile(string path)
    {
        try
        {
            var globalPath = ProjectSettings.GlobalizePath(path);
            if (!System.IO.File.Exists(globalPath)) return "JSON file not found";
            var content = System.IO.File.ReadAllText(globalPath);
            return ImportJson(content);
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

            if (TryResolveDifficulty(difficulties, songId, diffIdx, diffEnum.ToString(), out var diffNum))
            {
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
            if (_service.CurrentSave.GameSummary != null)
            {
                _service.CurrentSave.GameSummary.Rks = result;
            }
        }
        return result;
    }

    private static bool TryResolveDifficulty(Godot.Collections.Dictionary difficulties, string songId, int diffIdx, string diffName, out float diffNum)
    {
        diffNum = 0f;

        // Primary expected key format.
        var key = $"{songId}_{diffIdx}";
        if (TryGetDifficultyValue(difficulties, key, out diffNum)) return true;

        // Compatibility: string difficulty names from different data sources.
        if (!string.IsNullOrWhiteSpace(diffName))
        {
            key = $"{songId}_{diffName}";
            if (TryGetDifficultyValue(difficulties, key, out diffNum)) return true;

            var upper = diffName.ToUpperInvariant();
            key = $"{songId}_{upper}";
            if (TryGetDifficultyValue(difficulties, key, out diffNum)) return true;
        }

        // Compatibility aliases for common chart labels.
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
            if (TryGetDifficultyValue(difficulties, key, out diffNum)) return true;
        }

        return false;
    }

    private static bool TryGetDifficultyValue(Godot.Collections.Dictionary difficulties, string key, out float value)
    {
        value = 0f;
        if (!difficulties.ContainsKey(key)) return false;

        var v = difficulties[key];
        value = (float)v.AsDouble();
        return true;
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