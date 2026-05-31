using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp.CloudSave;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 异步任务包装器，用于管理异步并向GDScript发送完成或错误信号
/// </summary>
[GlobalClass]
public partial class AsyncPhiSaveRequest : RefCounted
{
    [Signal]
    public delegate void CompletedEventHandler(Variant result);

    [Signal]
    public delegate void ErrorEventHandler(string message);

    /// <summary>
    /// 将异步请求标记为完成并向 GDScript 发出 <c>Completed</c> 信号。
    /// </summary>
    /// <param name="result">要传递给监听者的结果值（Variant）。</param>
    public void Resolve(Variant result)
    {
        CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.Completed, result);
    }

    /// <summary>
    /// 将异步请求标记为失败并向 GDScript 发出 <c>Error</c> 信号。
    /// </summary>
    /// <param name="message">错误消息，供客户端显示或记录。</param>
    public void Reject(string message)
    {
        CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.Error, message);
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
    // ======信号绑定部分======
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
            try { CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.QrCodeGenerated, qr.Url, qr.ExpiresInSeconds); } catch { }
        };

        _service.QrCodeCheckResult += (res) =>
        {
            // Optionally forward check result as OAuthUrl or intermediate event; keep simple for now
            // No direct signal for each poll; UI can listen to QrCodeGenerated and OAuthLoginResult
        };

        _service.OAuthUrlGenerated += (url) =>
        {
            try { CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.OAuthUrlGenerated, url); } catch { }
        };

        _service.LoginCompleted += (success, tokenOrErr) =>
        {
            try
            {
                if (success) CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginSuccess, tokenOrErr, _service.UserObjectId);
                else CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginFailed, tokenOrErr ?? "");
            }
            catch { }
        };
    }

    // ======登录与sessiontoken获取部分======
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
                CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.QrCodeGenerated, qrcode.Url, qrcode.ExpiresInSeconds);

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
                            if (!succ) CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginFailed, tokenOrErr);
                        }
                        else
                        {
                            CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginFailed, "login_timeout");
                        }
                        break;
                    }
                    await Task.Delay(3000, cts.Token);
                }

                if (cts.IsCancellationRequested)
                {
                    CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginFailed, "timeout");
                }
            }
            catch (Exception ex)
            {
                CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.LoginFailed, ex.Message);
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
                    CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.OAuthLoginResult, succ, tokenOrErr);
                }
                else
                {
                    CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.OAuthLoginResult, false, "timeout");
                }
            }
            catch (Exception ex)
            {
                CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.OAuthLoginResult, false, ex.Message);
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

    // ======存档上传与下载======
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
    /// 类似于 git diff 的行为：比较本地与云端存档的差异，返回一个结构化的结果，包含每条差异的详情（如歌曲ID、难度、分数/准确率差异）以及全量存档的字段级差异，供客户端展示给用户并让用户选择如何解决。适用于需要在客户端展示差异详情并让用户选择如何合并的场景。
    /// </summary>
    public AsyncPhiSaveRequest DiffWithCloud()
    {
        var request = new AsyncPhiSaveRequest();

        Task.Run(async () =>
        {
            try
            {
                PhiSaveData? local = _service.CurrentSave;
                (PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();
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

    private Godot.Collections.Dictionary SerializeSummaryToGD(Summary? sum)
    {
        var dict = new Godot.Collections.Dictionary();
        if (sum == null) return dict;
        dict["gameVersion"] = sum.GameVersion;
        dict["rks"] = sum.Rks;
        dict["avatar"] = sum.Avatar ?? "";
        dict["challenge"] = (int)sum.Challenge.RawCode;
        return dict;
    }

    private static Godot.Collections.Dictionary CreateSongScoreDictionary(string prefix, SongScore? score)
    {
        var dict = new Godot.Collections.Dictionary();
        if (score == null) return dict;

        dict[$"{prefix}_obj"] = new Godot.Collections.Dictionary
        {
            { "Id", score.Id ?? "" },
            { "Score", score.Score },
            { "Accuracy", score.Accuracy },
            { "Difficulty", score.Difficulty.ToString() },
            { "Status", score.Status.ToString() }
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
    /// 同步本地与云端（自动选择上传/下载/合并），返回操作结果与可能的冲突 diff。
    /// 类似于 git push 的默认行为：如果没有冲突则直接上传；如果有冲突但 preferLocal=true 则以本地为准上传；如果有冲突但 preferLocal=false 则以云端为准下载；如果 allowMerge=true 则返回差异供客户端选择后合并。
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
    /// 输入格式为一个字典，键为 "{songId}_{difficultyIndex}"，值为 "local"、"cloud" 或 "higher"。
    /// 类似于 git merge --ours/theirs 或手动编辑冲突后 git add 的行为：根据用户选择的策略应用每条差异并上传合并结果。
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
    /// 适用于需要在客户端展示差异详情并让用户选择如何合并的场景。
    /// 类似于 git merge --no-commit --no-ff 后查看 git status 和 git diff 的行为：在服务端生成合并结果但不提交，返回差异详情供客户端展示，并提供接口让客户端选择后应用或丢弃预览结果。
    /// </summary>
    public AsyncPhiSaveRequest PreviewMergeWithCloud()
    {
        var request = new AsyncPhiSaveRequest();
        Task.Run(async () =>
        {
            try
            {
                PhiSaveData? local = _service.CurrentSave;
                (PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();

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
    /// 类似于 git status 后再次查看 git diff 的行为：在服务端已经有一个合并预览的情况下，客户端可以随时查询这个预览的详情以更新 UI 展示，而不需要每次都重新生成预览。
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

                PhiSaveData? local = _service.CurrentSave;
                (PhiSaveData? cloud, string? fileId, string? objId) = await _service.GetCloudSaveCopyAsync();
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
    /// 类似于 git merge --no-commit 后 git add 的行为：将之前生成的合并预览应用到当前内存缓存，但不自动上传，允许客户端在应用后再次查看预览详情以更新 UI，或者在应用后进行修改后再上传。
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
    /// 类似于 git merge --abort 的行为：丢弃服务端保存的合并预览，恢复到合并前的状态。适用于用户在查看预览后决定放弃合并，或者在应用预览后又想回退到原来状态的场景。
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
    /// 类似于 git merge --no-ff 后 git commit 的行为：将之前生成的合并预览应用到当前内存缓存，并立即上传到云端，完成整个合并流程。适用于用户在查看预览后直接确认合并并希望立即同步结果的场景。
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
    /// 类似于 git merge --no-ff 后 git commit 再 git archive 的行为：将之前生成的合并预览应用到当前内存缓存，并立即上传到云端，完成整个合并流程，并且将合并后的结果保存到本地文件，供用户备份或离线使用。适用于用户在确认合并后希望同时获得云端同步和本地备份的场景。
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
    /// 返回当前可供解决的差异键列表，格式为 "{songId}_{difficultyIndex}"。
    /// 类似于 git diff --name-only 的行为：在存在差异的情况下，返回一个列表，列出每条差异对应的唯一键，供客户端展示给用户并让用户选择如何解决。适用于需要在客户端展示差异详情并让用户选择如何合并的场景。
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

    // ======导入导出部分======
    // ======本地加密存档=======
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
            PhiSaveData? loaded = _service.LoadLocalFromFileAsync(globalPath, key, iv).GetAwaiter().GetResult();
            if (loaded != null) return "OK";
            return "File not found or format error.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ======JSON明文======
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
    /// 获取当前内存存档中指定 entry 的字典快照。
    /// 支持 gameRecord、gameProgress、gameUserInfo、gameSettings、gameKeys 及常用别名。
    /// </summary>
    /// <param name="entryName">条目名称。</param>
    /// <returns>指定 entry 的字典快照；如果当前没有存档则返回空字典。</returns>
    public Godot.Collections.Dictionary GetMemoryEntry(string entryName)
    {
        try
        {
            var entry = _service.GetMemoryEntry(entryName);
            return ToGodotDictionary(entry);
        }
        catch (Exception ex)
        {
            return new Godot.Collections.Dictionary
            {
                { "error", ex.Message }
            };
        }
    }

    /// <summary>
    /// 将字典写回当前内存存档中的指定 entry。
    /// </summary>
    /// <param name="entryName">条目名称。</param>
    /// <param name="entryData">要写入的字典内容。</param>
    /// <returns>写入成功返回 OK；失败返回错误信息。</returns>
    public string SetMemoryEntry(string entryName, Godot.Collections.Dictionary entryData)
    {
        try
        {
            var plain = ToPlainDictionary(entryData);
            return _service.SetMemoryEntry(entryName, plain) ? "OK" : "Invalid entry or data";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ======Summary二进制字符串======
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

    // ======RKS计算与信息获取======
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

    // ======辅助方法&TapTap APK链接获取======
    /// <summary>
    /// GDScript 辅助方法：检查当前是否加载了存档到内存
    /// </summary>
    public bool HasCurrentSave() => _service.CurrentSave != null;

    private static string ResolveGlobalPath(string path) => ProjectSettings.GlobalizePath(path);

    private static Godot.Collections.Dictionary ToGodotDictionary(Dictionary<string, object?>? source)
    {
        var result = new Godot.Collections.Dictionary();
        if (source == null) return result;

        foreach (var kv in source)
        {
            result[kv.Key] = ToGodotValue(kv.Value);
        }

        return result;
    }

    private static Dictionary<string, object?> ToPlainDictionary(Godot.Collections.Dictionary source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var key in source.Keys)
        {
            result[key.ToString() ?? string.Empty] = ToPlainValue(source[key]);
        }

        return result;
    }

    private static object? ToPlainValue(object? value)
    {
        return value switch
        {
            Variant variant => VariantToPlainValue(variant),
            null => null,
            Godot.Collections.Dictionary dict => ToPlainDictionary(dict),
            Godot.Collections.Array array => ToPlainList(array),
            Array array => ToPlainList(array),
            string stringValue => stringValue,
            bool boolValue => boolValue,
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => floatValue,
            double doubleValue => doubleValue,
            _ => value
        };
    }

    private static List<object?> ToPlainList(System.Collections.IEnumerable values)
    {
        var result = new List<object?>();
        foreach (var value in values)
        {
            result.Add(ToPlainValue(value));
        }

        return result;
    }

    private static Variant ToGodotValue(object? value)
    {
        return value switch
        {
            null => default,
            Variant variant => variant,
            Dictionary<string, object?> dict => ToGodotDictionary(dict),
            List<object?> list => ToGodotArray(list),
            System.Collections.IEnumerable enumerable when value is not string => ToGodotArray(enumerable),
            string stringValue => stringValue,
            bool boolValue => boolValue,
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => floatValue,
            double doubleValue => doubleValue,
            _ => value?.ToString() ?? string.Empty
        };
    }

    private static Godot.Collections.Array ToGodotArray(System.Collections.IEnumerable values)
    {
        var result = new Godot.Collections.Array();
        foreach (var value in values)
        {
            result.Add(ToGodotValue(value));
        }

        return result;
    }

    private static object? VariantToPlainValue(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Nil => null,
            Variant.Type.Bool => value.As<bool>(),
            Variant.Type.Int => value.As<long>(),
            Variant.Type.Float => value.As<double>(),
            Variant.Type.String => value.As<string>(),
            Variant.Type.Dictionary => ToPlainDictionary(value.As<Godot.Collections.Dictionary>()),
            Variant.Type.Array => ToPlainList(value.As<Godot.Collections.Array>()),
            _ => value.As<string>()
        };
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