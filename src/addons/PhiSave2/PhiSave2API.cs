using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhigrosLibraryCSharp.CloudSave;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;
using System.IO;

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

    private static string ResolveGlobalPath(string path) => ProjectSettings.GlobalizePath(path);

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
        // Subscribe to service events and forward to Godot signals
        _service.QrCodeAvailable += (qr) =>
        {
            try { CallDeferred(MethodName.EmitSignal, SignalName.QrCodeGenerated, qr.Url, qr.ExpiresInSeconds); } catch { }
        };

        _service.QrCodeCheckResult += (res) =>
        {
            // Optionally forward check result as OAuthUrl or intermediate event; keep simple for now
            // No direct signal for each poll; UI can listen to QrCodeGenerated and OAuthLoginResult
        };

        _service.OAuthUrlGenerated += (url) =>
        {
            try { CallDeferred(MethodName.EmitSignal, SignalName.OAuthUrlGenerated, url); } catch { }
        };

        _service.LoginCompleted += (success, tokenOrErr) =>
        {
            try
            {
                if (success) CallDeferred(MethodName.EmitSignal, SignalName.LoginSuccess, tokenOrErr, _service.UserObjectId);
                else CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, tokenOrErr ?? "");
            }
            catch { }
        };
    }

    /// <summary>
    /// 请求二维码并开始轮询登录
    /// 返回的信号将包含二维码 URL，使用外部 UI 渲染
    /// </summary>
    public void StartQrLogin()
    {
        Task.Run(async () =>
        {
            CancellationTokenSource? cts = null;
            TaskCompletionSource<(bool Success, string TokenOrError)>? tcs = null;
            void OnLogin(bool s, string t) { try { tcs?.TrySetResult((s, t ?? string.Empty)); } catch { } }
            try
            {
                cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                tcs = new TaskCompletionSource<(bool, string)>();
                _service.LoginCompleted += OnLogin;

                var qrcode = await _service.RequestQrCodeAsync();
                CallDeferred(MethodName.EmitSignal, SignalName.QrCodeGenerated, qrcode.Url, qrcode.ExpiresInSeconds);

                while (!cts.IsCancellationRequested)
                {
                    var taptapData = await _service.CheckQrCodeAsync(qrcode);
                    if (taptapData is not null)
                    {
                        // Let service perform CompleteLogin and emit LoginCompleted
                        await _service.CompleteLoginAsync(taptapData);

                        // wait for service to report login result (short timeout)
                        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), cts.Token));
                        if (completed == tcs.Task)
                        {
                            var (succ, tokenOrErr) = tcs.Task.Result;
                            if (!succ) CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, tokenOrErr);
                        }
                        else
                        {
                            CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, "login_timeout");
                        }
                        break;
                    }
                    await Task.Delay(3000, cts.Token);
                }

                if (cts.IsCancellationRequested)
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, "timeout");
                }
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.LoginFailed, ex.Message);
            }
            finally
            {
                try { if (tcs != null && !tcs.Task.IsCompleted) tcs.TrySetCanceled(); } catch { }
                try { _service.LoginCompleted -= OnLogin; } catch { }
                try { cts?.Dispose(); } catch { }
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
            CancellationTokenSource? cts = null;
            TaskCompletionSource<(bool Success, string TokenOrError)>? tcs = null;
            void OnLogin(bool s, string t) { try { tcs?.TrySetResult((s, t ?? string.Empty)); } catch { } }
            try
            {
                var target_auth = authEndpoint ?? "https://accounts.taptap.com/authorize";
                var target_token = tokenEndpoint ?? "https://accounts.tapapis.cn/oauth2/v1/token";
                var target_id = clientId ?? PhiSave2Service.DefaultClientId;
                var target_secret = clientSecret ?? PhiSave2Service.DefaultClientKey;

                cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                tcs = new TaskCompletionSource<(bool, string)>();
                _service.LoginCompleted += OnLogin;

                var url = await _service.StartOAuthFlowAsync(port, target_auth, target_token, target_id, target_secret, scope);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(60000, cts.Token));
                if (completed == tcs.Task)
                {
                    var (succ, tokenOrErr) = tcs.Task.Result;
                    CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, succ, tokenOrErr);
                }
                else
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, false, "timeout");
                }
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.OAuthLoginResult, false, ex.Message);
            }
            finally
            {
                try { if (tcs != null && !tcs.Task.IsCompleted) tcs.TrySetCanceled(); } catch { }
                try { _service.LoginCompleted -= OnLogin; } catch { }
                try { cts?.Dispose(); } catch { }
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
                var core = new TapTapApkLinkCore();
                var info = await core.GetDownloadUrlAsync(appId > 0 ? appId : core.DefaultAppId);
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
    /// 获取本地(内存)与云端存档差异，包含成绩差异与全量存档差异。
    /// </summary>
    public AsyncPhiSaveRequest DiffWithCloud()
    {
        var request = new AsyncPhiSaveRequest();

        Task.Run(async () =>
        {
            try
            {
                PhiStore.Addons.PhiSave2.Models.PhiSaveData? local = _service.CurrentSave;
                (PhiStore.Addons.PhiSave2.Models.PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();
                var diffs = await _service.GetDetailedDiffsWithObjectsAsync(local, cloud);
                var fullDiffs = await _service.DiffFullAsync(local, cloud);

                // fetch top-level metadata (modified times / rks)
                var md = await _service.GetSaveMetadataAsync(null);

                var arr = BuildDetailedDiffArray(diffs);

                var result = new Godot.Collections.Dictionary();
                result["score_diffs"] = arr;

                var fullArr = BuildFullDiffArray(fullDiffs);
                result["full_diffs"] = fullArr;
                result["full_diff_count"] = fullArr.Count;

                result["local_summary"] = SerializeSummaryToGD(local?.GameSummary);
                result["cloud_summary"] = SerializeSummaryToGD(cloud?.GameSummary);

                // include save metadata for client decision making
                result["local_modified_utc"] = md.LocalModifiedUtc?.ToString("o") ?? "";
                result["cloud_modified_utc"] = md.CloudModifiedUtc?.ToString("o") ?? "";
                result["local_rks"] = md.LocalRks;
                result["cloud_rks"] = md.CloudRks;
                result["fileId"] = fileId ?? "";
                result["objId"] = objId ?? "";

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

    private static Godot.Collections.Dictionary CreateSongScoreDictionary(string prefix, PhigrosLibraryCSharp.CloudSave.SongScore? score)
    {
        var dict = new Godot.Collections.Dictionary();
        if (score == null) return dict;

        dict[$"{prefix}_obj"] = new Godot.Collections.Dictionary
        {
            { "Id", score.Id ?? "" },
            { "Score", score.Score },
            { "Accuracy", score.Accuracy },
            { "Difficulty", (int)score.Difficulty },
            { "Status", (int)score.Status }
        };
        return dict;
    }

    private static Godot.Collections.Dictionary BuildDetailedDiffDictionary(PhiSave2Service.DetailedScoreDiffWithObjects item)
    {
        var d = item.Diff;
        var diffDict = new Godot.Collections.Dictionary
        {
            ["songId"] = d.SongId,
            ["difficulty"] = d.DifficultyIndex,
            ["local_score"] = d.LocalScore.HasValue ? d.LocalScore.Value : -1,
            ["local_acc"] = d.LocalAcc.HasValue ? d.LocalAcc.Value : -1f,
            ["cloud_score"] = d.CloudScore.HasValue ? d.CloudScore.Value : -1,
            ["cloud_acc"] = d.CloudAcc.HasValue ? d.CloudAcc.Value : -1f,
            ["suggestion"] = d.Suggestion ?? ""
        };

        foreach (var kv in CreateSongScoreDictionary("local", item.LocalObj)) diffDict[kv.Key] = kv.Value;
        foreach (var kv in CreateSongScoreDictionary("cloud", item.CloudObj)) diffDict[kv.Key] = kv.Value;

        return diffDict;
    }

    private static Godot.Collections.Array BuildDetailedDiffArray(IEnumerable<PhiSave2Service.DetailedScoreDiffWithObjects> diffs)
    {
        var arr = new Godot.Collections.Array();
        foreach (var item in diffs)
        {
            arr.Add(BuildDetailedDiffDictionary(item));
        }
        return arr;
    }

    private static Godot.Collections.Array BuildFullDiffArray(IEnumerable<PhiSave2Service.FullDiff> fullDiffs)
    {
        var arr = new Godot.Collections.Array();
        foreach (var fd in fullDiffs)
        {
            arr.Add(new Godot.Collections.Dictionary
            {
                { "path", fd.Path },
                { "type", fd.DiffType },
                { "local_value", fd.LocalValue ?? string.Empty },
                { "cloud_value", fd.CloudValue ?? string.Empty }
            });
        }
        return arr;
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
                await _service.MergeAndSyncAsync(null);
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
    /// 合并本地与云端并同步到指定本地文件路径。
    /// </summary>
    public AsyncPhiSaveRequest MergeWithCloudToLocal(string localPath)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                await _service.MergeAndSyncAsync(ResolveGlobalPath(localPath));
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
                (PhiStore.Addons.PhiSave2.Models.PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();
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
        try
        {
            var globalPath = ResolveGlobalPath(path);
            var ok = _service.SaveLocalToFileAsync(globalPath, key, iv).GetAwaiter().GetResult();
            return ok ? "OK" : "WriteFailed";
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
            var globalPath = ResolveGlobalPath(path);
            PhiStore.Addons.PhiSave2.Models.PhiSaveData? loaded = _service.LoadLocalFromFileAsync(globalPath, key, iv).GetAwaiter().GetResult();
            if (loaded != null) return "OK";
            return "File not found or format error.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public Godot.Collections.Array ListLocalSaves(string directory, string pattern = "*")
    {
        var arr = new Godot.Collections.Array();
        try
        {
            var full = ResolveGlobalPath(directory);
            var list = _service.ListLocalSavesAsync(full, pattern).GetAwaiter().GetResult();
            foreach (var f in list) arr.Add(f);
        }
        catch { }
        return arr;
    }

    public bool DeleteLocalSave(string path)
    {
        try
        {
            var full = ResolveGlobalPath(path);
            return _service.DeleteLocalFileAsync(full).GetAwaiter().GetResult();
        }
        catch { return false; }
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
    /// 导出当前 Summary 为二进制并以 Base64 返回
    /// </summary>
    public string ExportSummaryBase64()
    {
        try
        {
            var bytes = _service.SerializeCurrentSummaryToBytes();
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Convert.ToBase64String(bytes);
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 将当前 Summary 二进制导出到文件（原始二进制，不做压缩）
    /// </summary>
    public string ExportSummaryToFile(string path)
    {
        try
        {
            var global = ResolveGlobalPath(path);
            var ok = _service.SaveSummaryToFile(global);
            return ok ? "OK" : "No summary or write failed";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// 从 Base64 字符串导入 Summary 二进制并应用到内存
    /// </summary>
    public string ImportSummaryBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var ok = _service.ApplySummaryBytesToCurrentSave(bytes);
            return ok ? "OK" : "ParseError";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// 导出 JSON 存档到文件
    /// </summary>
    public string ExportJsonToFile(string path)
    {
        try
        {
            var globalPath = ResolveGlobalPath(path);
            var ok = _service.SaveJsonToFile(globalPath);
            return ok ? "OK" : "No save in memory or write failed";
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
    /// 导出当前内存为 JSON 并可使用 AES 密钥加密保存到文件。
    /// keyBase64 / ivBase64 可选，若为空则使用内部默认调试密钥。
    /// 返回 "OK" 或错误信息。
    /// </summary>
    public string ExportJsonEncryptedToFile(string path, string? keyBase64 = null, string? ivBase64 = null)
    {
        if (_service.CurrentSave is null) return "No save in memory.";
        try
        {
            var key = string.IsNullOrEmpty(keyBase64) ? null : Convert.FromBase64String(keyBase64);
            var iv = string.IsNullOrEmpty(ivBase64) ? null : Convert.FromBase64String(ivBase64);
            var globalPath = ResolveGlobalPath(path);
            var ok = _service.SaveJsonEncryptedToFile(globalPath, key, iv);
            return ok ? "OK" : "Write failed";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 从 AES 加密的 JSON 文件导入存档。keyBase64 / ivBase64 与导出时保持一致。
    /// 返回 "OK" 或错误信息。
    /// </summary>
    public string ImportJsonFromEncryptedFile(string path, string? keyBase64 = null, string? ivBase64 = null)
    {
        try
        {
            var key = string.IsNullOrEmpty(keyBase64) ? null : Convert.FromBase64String(keyBase64);
            var iv = string.IsNullOrEmpty(ivBase64) ? null : Convert.FromBase64String(ivBase64);
            var globalPath = ResolveGlobalPath(path);
            var ok = _service.LoadJsonEncryptedFromFile(globalPath, key, iv);
            return ok ? "OK" : "File not found or format error.";
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
            var globalPath = ResolveGlobalPath(path);
            var ok = _service.LoadJsonFromFile(globalPath);
            return ok ? "OK" : "JSON file not found or parse error";
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
        // convert Godot dictionary to plain dictionary and delegate to service
        var map = new Dictionary<string, float>();
        try
        {
            foreach (var k in difficulties.Keys)
            {
                var key = k.ToString();
                var v = difficulties[k];
                var val = (float)v.AsDouble();
                map[key] = val;
            }
        }
        catch
        {
            return 0f;
        }

        return _service.ComputeAndApplyInMemoryRks(map);
    }

    /// <summary>
    /// 获取 RKS 详细信息：每首歌的计算条目与总体汇总（B27 / top3 Phi / total）
    /// difficulties: Dictionary key -> difficulty value (float)
    /// </summary>
    public Godot.Collections.Dictionary GetRksDetails(Godot.Collections.Dictionary difficulties)
    {
        var outd = new Godot.Collections.Dictionary();
        try
        {
            var map = new Dictionary<string, float>();
            foreach (var k in difficulties.Keys)
            {
                var key = k.ToString();
                var v = difficulties[k];
                var val = (float)v.AsDouble();
                map[key] = val;
            }

            var details = _service.CalculateRksDetails(map);

            var arr = new Godot.Collections.Array();
            foreach (var e in details.Entries)
            {
                var ed = new Godot.Collections.Dictionary
                {
                    { "songId", e.SongId },
                    { "difficultyIndex", e.DifficultyIndex },
                    { "difficultyName", e.DifficultyName },
                    { "difficultyValue", e.DifficultyValue },
                    { "acc", e.Acc },
                    { "rks", e.Rks },
                    { "isPhi", e.IsPhi }
                };
                arr.Add(ed);
            }

            outd["entries"] = arr;
            outd["sumB27"] = details.SumB27;
            outd["sumTop3Phi"] = details.SumTop3Phi;
            outd["totalRks"] = details.TotalRks;
        }
        catch (Exception ex)
        {
            outd["error"] = ex.Message;
        }
        return outd;
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
    /// 同步本地与云端（自动选择上传/下载/合并），返回操作结果与可能的冲突 diff
    /// </summary>
    public AsyncPhiSaveRequest SyncWithCloud(bool preferLocal = false, bool allowMerge = true)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var res = await _service.SyncWithCloudAsync(preferLocal, allowMerge);
                var dict = new Godot.Collections.Dictionary
                {
                    { "status", res.Status },
                    { "message", res.Message },
                    { "fileId", res.FileId ?? "" },
                    { "objId", res.ObjId ?? "" }
                };
                if (res.Diffs != null)
                {
                    var arr = new Godot.Collections.Array();
                    foreach (var d in res.Diffs)
                    {
                        var dd = new Godot.Collections.Dictionary
                        {
                            { "songId", d.SongId },
                            { "difficulty", d.DifficultyIndex },
                            { "local_score", d.LocalScore ?? -1 },
                            { "local_acc", d.LocalAcc ?? -1f },
                            { "cloud_score", d.CloudScore ?? -1 },
                            { "cloud_acc", d.CloudAcc ?? -1f }
                        };
                        arr.Add(dd);
                    }
                    dict["diffs"] = arr;
                }
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
    /// 根据 UI 提供的逐条选择应用冲突解决。
    /// choices: Dictionary<string,string> key->"local"|"cloud"|"higher"
    /// </summary>
    public AsyncPhiSaveRequest ResolveDiffs(Godot.Collections.Dictionary choices)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                // Validate input shape
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "local", "cloud", "higher" };
                var invalid = new Godot.Collections.Array();
                var dict = new Dictionary<string, string>();
                foreach (var k in choices.Keys)
                {
                    var key = k.ToString();
                    var v = choices[k];
                    var sval = v.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key)) { invalid.Add($"empty_key"); continue; }
                    if (!allowed.Contains(sval)) { invalid.Add($"{key}:{sval}"); continue; }
                    dict[key] = sval;
                }

                if (invalid.Count > 0)
                {
                    var outdErr = new Godot.Collections.Dictionary
                    {
                        { "status", "Error" },
                        { "message", "Invalid choices: " + string.Join(", ", invalid) }
                    };
                    request.Resolve(outdErr);
                    return;
                }

                var res = await _service.ResolveDiffsAndApplyAsync(dict);
                var outd = new Godot.Collections.Dictionary
                {
                    { "status", res.Status },
                    { "message", res.Message },
                    { "fileId", res.FileId ?? "" },
                    { "objId", res.ObjId ?? "" }
                };
                request.Resolve(outd);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });
        return request;
    }

    /// <summary>
    /// 生成合并预览（不应用）。返回合并后的 summary、score/full diff 与元数据。
    /// </summary>
    public AsyncPhiSaveRequest PreviewMergeWithCloud()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                PhiStore.Addons.PhiSave2.Models.PhiSaveData? local = _service.CurrentSave;
                (PhiStore.Addons.PhiSave2.Models.PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();

                // create preview and keep it in service
                var merged = await _service.CreateMergePreviewAsync(local, cloud);
                if (merged == null) throw new Exception("Failed to create merge preview");

                // build detailed diffs for client UI
                var diffs = await _service.GetDetailedDiffsWithObjectsAsync(local, cloud);
                var arr = BuildDetailedDiffArray(diffs);

                // fetch top-level metadata (modified times / rks)
                var md = await _service.GetSaveMetadataAsync(null);

                var dict = new Godot.Collections.Dictionary();
                dict["merged_record_count"] = merged.Record?.Records?.Count ?? 0;
                dict["merged_summary"] = SerializeSummaryToGD(merged.GameSummary);
                dict["score_diffs"] = arr;
                dict["local_modified_utc"] = md.LocalModifiedUtc?.ToString("o") ?? "";
                dict["cloud_modified_utc"] = md.CloudModifiedUtc?.ToString("o") ?? "";
                dict["local_rks"] = md.LocalRks;
                dict["cloud_rks"] = md.CloudRks;
                dict["fileId"] = fileId ?? "";
                dict["objId"] = objId ?? "";

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
    /// 获取当前已创建的合并预览详情（如果有），返回与 PreviewMergeWithCloud 相同结构的数据。
    /// 非创建操作，仅供客户端查询当前保存在 service 中的预览信息。
    /// </summary>
    public AsyncPhiSaveRequest GetPreviewDetails()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var preview = _service.GetPreviewMerge();
                if (preview == null)
                {
                    var empty = new Godot.Collections.Dictionary { { "status", "NoPreview" } };
                    request.Resolve(empty);
                    return;
                }

                PhiStore.Addons.PhiSave2.Models.PhiSaveData? local = _service.CurrentSave;
                (PhiStore.Addons.PhiSave2.Models.PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();
                var diffs = await _service.GetDetailedDiffsWithObjectsAsync(local, cloud);

                var arr = BuildDetailedDiffArray(diffs);

                var md = await _service.GetSaveMetadataAsync(null);

                var dict = new Godot.Collections.Dictionary();
                dict["merged_record_count"] = preview.Record?.Records?.Count ?? 0;
                dict["merged_summary"] = SerializeSummaryToGD(preview.GameSummary);
                dict["score_diffs"] = arr;
                dict["local_modified_utc"] = md.LocalModifiedUtc?.ToString("o") ?? "";
                dict["cloud_modified_utc"] = md.CloudModifiedUtc?.ToString("o") ?? "";
                dict["local_rks"] = md.LocalRks;
                dict["cloud_rks"] = md.CloudRks;
                dict["fileId"] = fileId ?? "";
                dict["objId"] = objId ?? "";

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
    /// 应用先前创建的合并预览到内存（不上传）
    /// </summary>
    public AsyncPhiSaveRequest ApplyPreviewMerge()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(() =>
        {
            try
            {
                var ok = _service.ApplyPreviewMerge();
                request.Resolve(ok);
            }
            catch (Exception ex) { request.Reject(ex.Message); }
        });
        return request;
    }

    /// <summary>
    /// 丢弃当前合并预览
    /// </summary>
    public AsyncPhiSaveRequest DiscardPreviewMerge()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(() =>
        {
            try
            {
                var ok = _service.DiscardPreviewMerge();
                request.Resolve(ok);
            }
            catch (Exception ex) { request.Reject(ex.Message); }
        });
        return request;
    }

    /// <summary>
    /// 应用当前合并预览并上传到云端
    /// </summary>
    public AsyncPhiSaveRequest ApplyPreviewAndUpload(string? oldFileId = null, string? oldObjId = null)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var ok = await _service.ApplyPreviewAndUploadAsync(oldFileId ?? _oldFileId, oldObjId ?? _oldObjId);
                request.Resolve(ok);
            }
            catch (Exception ex) { request.Reject(ex.Message); }
        });
        return request;
    }

    /// <summary>
    /// 应用当前合并预览，上传到云端并将结果保存到指定本地路径。
    /// </summary>
    public AsyncPhiSaveRequest ApplyPreviewAndUploadToLocal(string localPath, string? oldFileId = null, string? oldObjId = null)
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                var ok = await _service.ApplyPreviewAndUploadAsync(oldFileId ?? _oldFileId, oldObjId ?? _oldObjId, ResolveGlobalPath(localPath));
                request.Resolve(ok);
            }
            catch (Exception ex) { request.Reject(ex.Message); }
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

    /// <summary>
    /// 返回当前可供解决的差异键列表，格式为 "{songId}_{difficultyIndex}"。
    /// </summary>
    public Godot.Collections.Array GetAvailableDiffKeys()
    {
        var arr = new Godot.Collections.Array();
        try
        {
            var list = _service.GetCurrentDiffKeysAsync().GetAwaiter().GetResult();
            foreach (var k in list) arr.Add(k);
        }
        catch { }
        return arr;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _service.Dispose();
        base.Dispose(disposing);
    }
}