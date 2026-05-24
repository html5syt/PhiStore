using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;
using PhiStore.Addons.PhiSave2.Models;
using PhigrosLibraryCSharp.Serialization;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
    /// <summary>
    /// 同步云端存档
    /// </summary>
    public async Task<(string fileId, string objId)> DownloadSaveAsync()
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");
        // If we still don't have a user object id, try LeanCloud users/me as a best-effort fallback
        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(_sessionToken))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }

        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        if (container.Results.Count == 0)
        {
            // 如果初步查询为空，尝试显式获取一次用户信息并重试。
            // 某些情况下，Session Token 需要激活或 User ID 需要明确加载。
            if (string.IsNullOrWhiteSpace(_userObjectId))
            {
                _userObjectId = await FetchUserObjectIdFromServerAsync();
            }
            container = await _saveObj.GetSaveInfoFromCloudAsync();
            if (container.Results.Count == 0)
            {
                throw new Exception("No cloud save found for this account. Ensure your server selection and game account match.");
            }
        }

        var targetIndex = -1;
        SaveContext? ctx = null;
        Exception? lastContextError = null;
        for (var i = 0; i < container.Results.Count; i++)
        {
            try
            {
                var candidateCtx = await _saveObj.GetSaveContextAsync(i);
                if (candidateCtx != null)
                {
                    targetIndex = i;
                    ctx = candidateCtx;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastContextError = ex;
            }
        }

        if (targetIndex < 0 || ctx == null)
        {
            var detail = lastContextError != null ? $" Last error: {lastContextError.Message}" : string.Empty;
            throw new Exception("Failed to load any cloud save context from server." + detail);
        }

        var info = container.Results[targetIndex];
        if (!string.IsNullOrWhiteSpace(info?.User?.ObjectId))
        {
            _userObjectId = info.User.ObjectId;
        }

        CurrentSave = PhiSaveData.FromSaveContext(ctx);
        // populate user object id from returned info if still missing
        try
        {
            if (string.IsNullOrWhiteSpace(_userObjectId) && info?.User?.ObjectId != null)
            {
                _userObjectId = info.User.ObjectId;
            }
        }
        catch { }

        string fileId = string.Empty;
        string objId = string.Empty;
        if (info != null)
        {
            if (info.GameFile != null && info.GameFile.ObjectId != null) fileId = info.GameFile.ObjectId;
            if (info.ObjectId != null) objId = info.ObjectId;
        }
        return (fileId, objId);
    }

    /// <summary>
    /// 获取本地与云端的存档元信息（修改时间与 Summary.Rks）
    /// localFilePath 可选：如果提供则读取其文件修改时间；否则使用内存 CurrentSave
    /// </summary>
    public async Task<SaveMetadata> GetSaveMetadataAsync(string? localFilePath = null)
    {
        DateTime? localTime = null;
        float localRks = 0f;

        if (CurrentSave != null)
        {
            localTime = CurrentSave.ModifiedAt;
            localRks = CurrentSave.SummaryRks;
        }
        else if (!string.IsNullOrEmpty(localFilePath) && File.Exists(localFilePath))
        {
            localTime = File.GetLastWriteTimeUtc(localFilePath);
        }
        else if (CurrentSave != null)
        {
            localRks = CurrentSave.SummaryRks;
        }

        DateTime? cloudTime = null;
        float cloudRks = 0f;
        if (_saveObj != null)
        {
            var container = await _saveObj.GetSaveInfoFromCloudAsync();
            if (container.Results.Count > 0)
            {
                var info = container.Results[0];
                cloudTime = await FetchCloudModifiedUtcFromRawAsync();
                try
                {
                    if (cloudTime == null)
                    {
                        var mod = info.ModifiedAt;
                        if (mod != null)
                        {
                            var timestr = mod.ToString();
                            if (!string.IsNullOrEmpty(timestr))
                            {
                                try
                                {
                                    var isoKey = "\"iso\"";
                                    var idx = timestr.IndexOf(isoKey, StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        var colon = timestr.IndexOf(':', idx + isoKey.Length);
                                        if (colon >= 0)
                                        {
                                            var firstQuote = timestr.IndexOf('"', colon + 1);
                                            if (firstQuote >= 0)
                                            {
                                                var secondQuote = timestr.IndexOf('"', firstQuote + 1);
                                                if (secondQuote > firstQuote)
                                                {
                                                    var iso = timestr.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                                                    if (DateTime.TryParse(iso, out var parsedF)) cloudTime = parsedF.ToUniversalTime();
                                                }
                                            }
                                        }
                                    }
                                    else if (DateTime.TryParse(timestr, out var parsedF2))
                                    {
                                        cloudTime = parsedF2.ToUniversalTime();
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    var sumField = info.Summary as string;
                    if (!string.IsNullOrEmpty(sumField))
                    {
                        var raw = Convert.FromBase64String(sumField);
                        var br = new ByteReader(raw);
                        var sum = Summary.FromReader(br);
                        cloudRks = sum.Rks;
                    }
                }
                catch { }
            }
        }

        return new SaveMetadata(localTime, cloudTime, localRks, cloudRks);
    }

    /// <summary>
    /// 同步本地与云端，根据参数选择优先策略或尝试合并。
    /// 返回 SyncResult 描述操作结果：Uploaded/Downloaded/Merged/Conflict/NoOp
    /// </summary>
    public async Task<SyncResult> SyncWithCloudAsync(bool preferLocal = false, bool allowMerge = true)
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");

        var (cloudCopy, fileId, objId) = await GetCloudSaveCopyAsync();
        var local = CurrentSave;

        if (cloudCopy == null && local == null)
            return new SyncResult("NoOp", "No save locally or in cloud.");

        if (cloudCopy == null && local != null)
        {
            await UploadSaveAsync(null, null);
            return new SyncResult("Uploaded", "Uploaded local save to cloud.");
        }

        if (local == null && cloudCopy != null)
        {
            CurrentSave = cloudCopy;
            return new SyncResult("Downloaded", "Downloaded cloud save to local.", null, fileId, objId);
        }

        var md = await GetSaveMetadataAsync(null);
        if (preferLocal)
        {
            await UploadSaveAsync(fileId, objId);
            return new SyncResult("Uploaded", "Prefer-local: uploaded local save to cloud.", null, fileId, objId);
        }

        if (md.CloudModifiedUtc != null && md.LocalModifiedUtc != null && md.CloudModifiedUtc > md.LocalModifiedUtc)
        {
            CurrentSave = cloudCopy;
            return new SyncResult("Downloaded", "Cloud is newer: downloaded cloud save.", null, fileId, objId);
        }

        if (Math.Abs(md.LocalRks - md.CloudRks) > 0.0001f)
        {
            if (md.LocalRks > md.CloudRks)
            {
                await UploadSaveAsync(fileId, objId);
                return new SyncResult("Uploaded", "Local has higher RKS: uploaded.", null, fileId, objId);
            }
            else
            {
                CurrentSave = cloudCopy;
                return new SyncResult("Downloaded", "Cloud has higher RKS: downloaded.", null, fileId, objId);
            }
        }

        var diffs = await DiffSavesAsync(local, cloudCopy);
        if (diffs.Count == 0)
        {
            return new SyncResult("NoOp", "Saves are equivalent.");
        }

        if (allowMerge)
        {
            var merged = MergeSaves(local, cloudCopy);
            CurrentSave = merged;
            await UploadSaveAsync(fileId, objId);
            return new SyncResult("Merged", "Merged local and cloud saves and uploaded.", diffs, fileId, objId);
        }

        return new SyncResult("Conflict", "Conflict detected. Manual resolution required.", diffs, fileId, objId);
    }

    /// <summary>
    /// 直接合并当前本地与云端存档，并将结果同时同步到云端和本地文件（若提供 localPath）。
    /// </summary>
    public async Task<SyncResult> MergeAndSyncAsync(string? localPath = null, string? oldFileId = null, string? oldObjId = null)
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");

        var (cloudCopy, fileId, objId) = await GetCloudSaveCopyAsync();
        var local = CurrentSave;
        if (cloudCopy == null && local == null) return new SyncResult("NoOp", "No save locally or in cloud.");
        if (local == null && cloudCopy != null)
        {
            CurrentSave = cloudCopy;
            if (!string.IsNullOrWhiteSpace(localPath))
                await SaveLocalToFileAsync(localPath);
            return new SyncResult("Downloaded", "Merged flow fell back to cloud download.", null, fileId, objId);
        }
        if (cloudCopy == null && local != null)
        {
            await UploadSaveAsync(oldFileId ?? fileId, oldObjId ?? objId);
            if (!string.IsNullOrWhiteSpace(localPath))
                await SaveLocalToFileAsync(localPath);
            return new SyncResult("Uploaded", "Merged flow fell back to local upload.", null, fileId, objId);
        }

        var merged = MergeSaves(local, cloudCopy);
        CurrentSave = merged;

        await UploadSaveAsync(oldFileId ?? fileId, oldObjId ?? objId);

        if (!string.IsNullOrWhiteSpace(localPath))
        {
            try
            {
                await SaveLocalToFileAsync(localPath);
            }
            catch (Exception ex)
            {
                return new SyncResult("Merged", $"Merged and uploaded, but local save failed: {ex.Message}", null, fileId, objId);
            }
        }

        return new SyncResult("Merged", "Merged local and cloud saves and synchronized both targets.", null, fileId, objId);
    }

    /// <summary>
    /// 根据用户选择逐条解决差异并上传结果。
    /// choices: key => "local" | "cloud" | "higher"
    /// key 格式与 DiffSavesAsync 保持一致："{songId}_{difficultyIndex}"。
    /// </summary>
    public async Task<SyncResult> ResolveDiffsAndApplyAsync(Dictionary<string, string> choices)
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");

        var (cloudCopy, fileId, objId) = await GetCloudSaveCopyAsync();
        var local = CurrentSave;
        if (cloudCopy == null || local == null) return new SyncResult("Error", "Missing local or cloud save for resolution.");

        if (choices == null || choices.Count == 0)
            return new SyncResult("Error", "No choices provided for resolution.");

        var mapLocal = new Dictionary<string, SongScore>();
        foreach (var s in local.Record?.Records ?? new List<SongScore>())
        {
            mapLocal[$"{s.Id}_{(int)s.Difficulty}"] = s;
        }

        var mapCloud = new Dictionary<string, SongScore>();
        foreach (var s in cloudCopy.Record?.Records ?? new List<SongScore>())
        {
            mapCloud[$"{s.Id}_{(int)s.Difficulty}"] = s;
        }

        var mergedRecords = new Dictionary<string, SongScore>();
        foreach (var kv in mapCloud) mergedRecords[kv.Key] = kv.Value;
        foreach (var kv in mapLocal) if (!mergedRecords.ContainsKey(kv.Key)) mergedRecords[kv.Key] = kv.Value;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "local", "cloud", "higher" };
        var invalidKeys = new List<string>();
        var invalidValues = new List<string>();
        foreach (var kv in choices)
        {
            var key = kv.Key;
            var decisionRaw = kv.Value ?? string.Empty;
            var decision = decisionRaw.ToLowerInvariant();

            if (!mapLocal.ContainsKey(key) && !mapCloud.ContainsKey(key))
            {
                invalidKeys.Add(key);
                continue;
            }

            if (!allowed.Contains(decision))
            {
                invalidValues.Add(key + ":" + decisionRaw);
                continue;
            }

            if (decision == "local")
            {
                if (mapLocal.TryGetValue(key, out var lr)) mergedRecords[key] = lr;
            }
            else if (decision == "cloud")
            {
                if (mapCloud.TryGetValue(key, out var cr)) mergedRecords[key] = cr;
            }
            else if (decision == "higher")
            {
                mapLocal.TryGetValue(key, out var lr);
                mapCloud.TryGetValue(key, out var cr);
                if (lr != null && cr != null)
                {
                    if (lr.Score > cr.Score || (lr.Score == cr.Score && lr.Accuracy > cr.Accuracy)) mergedRecords[key] = lr;
                    else mergedRecords[key] = cr;
                }
                else if (lr != null) mergedRecords[key] = lr;
            }
        }

        if (invalidKeys.Count > 0)
            return new SyncResult("Error", "Invalid choice keys: " + string.Join(", ", invalidKeys));
        if (invalidValues.Count > 0)
            return new SyncResult("Error", "Invalid choice values (key:val): " + string.Join(", ", invalidValues));

        var result = new PhiSaveData();
        result.Record = new GameRecord(mergedRecords.Values.ToList(), 0);
        result.Progress = local.Progress ?? cloudCopy.Progress;
        result.UserInfo = local.UserInfo ?? cloudCopy.UserInfo;
        result.Settings = local.Settings ?? cloudCopy.Settings;

        var keys = new Dictionary<string, GameKeyFlag>();
        if (cloudCopy?.Keys != null) foreach (var kv in cloudCopy.Keys) keys[kv.Key] = kv.Value;
        if (local?.Keys != null) foreach (var kv in local.Keys) keys[kv.Key] = kv.Value;
        result.Keys = keys;

        result.SummaryRks = Math.Max(local?.SummaryRks ?? 0f, cloudCopy?.SummaryRks ?? 0f);

        CurrentSave = result;

        await UploadSaveAsync(fileId, objId);

        return new SyncResult("Resolved", "Applied selections and uploaded merged save.", null, fileId, objId);
    }

    /// <summary>
    /// 计算本地与云端的差异（主要 song score 差异），用于 diff 展示
    /// </summary>
    public async Task<List<ScoreDiff>> DiffSavesAsync(PhiSaveData? local, PhiSaveData? cloud)
    {
        var diffs = new List<ScoreDiff>();
        if (local == null && cloud == null) return diffs;

        var mapLocal = new Dictionary<string, (int Score, float Acc)>();
        if (local?.Record?.Records != null)
        {
            foreach (var s in local.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mapLocal[key] = (s.Score, s.Accuracy);
            }
        }

        var mapCloud = new Dictionary<string, (int Score, float Acc)>();
        if (cloud?.Record?.Records != null)
        {
            foreach (var s in cloud.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mapCloud[key] = (s.Score, s.Accuracy);
            }
        }

        var keys = new HashSet<string>(mapLocal.Keys);
        keys.UnionWith(mapCloud.Keys);

        foreach (var k in keys)
        {
            mapLocal.TryGetValue(k, out var l);
            mapCloud.TryGetValue(k, out var c);
            if (l.Score != c.Score || Math.Abs(l.Acc - c.Acc) > 0.0001f)
            {
                var parts = k.Split('_');
                var songId = parts[0];
                var diffIdx = int.Parse(parts[1]);
                diffs.Add(new ScoreDiff(songId, diffIdx, mapLocal.ContainsKey(k) ? (int?)l.Score : null, mapLocal.ContainsKey(k) ? (float?)l.Acc : null, mapCloud.ContainsKey(k) ? (int?)c.Score : null, mapCloud.ContainsKey(k) ? (float?)c.Acc : null));
            }
        }

        return diffs;
    }

    /// <summary>
    /// 全量比较：除了成绩外还比较 Progress / UserInfo / Settings / Keys / SummaryRks
    /// 返回路径与局部/云端的序列化值，供上层 UI 显示差异或逐项合并。
    /// </summary>
    public async Task<List<FullDiff>> DiffFullAsync(PhiSaveData? local, PhiSaveData? cloud)
    {
        var result = new List<FullDiff>();

        var scoreDiffs = await DiffSavesAsync(local, cloud);
        foreach (var sd in scoreDiffs) result.Add(new FullDiff($"records/{sd.SongId}/{sd.DifficultyIndex}", "score", sd.LocalScore?.ToString(), sd.LocalAcc?.ToString() ?? sd.CloudAcc?.ToString()));

        var lRks = local?.SummaryRks;
        var cRks = cloud?.SummaryRks;
        if (Math.Abs((lRks ?? 0f) - (cRks ?? 0f)) > 0.0001f)
            result.Add(new FullDiff("summary/rks", "summary_rks", lRks?.ToString(), cRks?.ToString()));

        var lp = local?.Progress != null ? JsonSerializer.Serialize(local.Progress, PhiSaveJsonContext.Default.GameProgress) : null;
        var cp = cloud?.Progress != null ? JsonSerializer.Serialize(cloud.Progress, PhiSaveJsonContext.Default.GameProgress) : null;
        if (lp != cp) result.Add(new FullDiff("progress", "progress", lp, cp));

        var lu = local?.UserInfo != null ? JsonSerializer.Serialize(local.UserInfo, PhiSaveJsonContext.Default.GameUserInfo) : null;
        var cu = cloud?.UserInfo != null ? JsonSerializer.Serialize(cloud.UserInfo, PhiSaveJsonContext.Default.GameUserInfo) : null;
        if (lu != cu) result.Add(new FullDiff("userInfo", "userInfo", lu, cu));

        var ls = local?.Settings != null ? JsonSerializer.Serialize(local.Settings, PhiSaveJsonContext.Default.GameSettings) : null;
        var cs = cloud?.Settings != null ? JsonSerializer.Serialize(cloud.Settings, PhiSaveJsonContext.Default.GameSettings) : null;
        if (ls != cs) result.Add(new FullDiff("settings", "settings", ls, cs));

        var lkeys = local?.Keys != null ? string.Join(",", local.Keys.Keys.OrderBy(x => x)) : string.Empty;
        var ckeys = cloud?.Keys != null ? string.Join(",", cloud.Keys.Keys.OrderBy(x => x)) : string.Empty;
        if (lkeys != ckeys) result.Add(new FullDiff("keys", "keys", lkeys, ckeys));

        return result;
    }

    /// <summary>
    /// 返回带建议（suggestion）的详细差异列表，建议为 "local" | "cloud" | "higher" | "equal"
    /// </summary>
    public async Task<List<DetailedScoreDiff>> GetDetailedDiffsAsync(PhiSaveData? local, PhiSaveData? cloud)
    {
        var diffs = await DiffSavesAsync(local, cloud);
        var list = new List<DetailedScoreDiff>();

        var mapLocal = new Dictionary<string, SongScore>();
        if (local?.Record?.Records != null)
            foreach (var s in local.Record.Records) mapLocal[$"{s.Id}_{(int)s.Difficulty}"] = s;

        var mapCloud = new Dictionary<string, SongScore>();
        if (cloud?.Record?.Records != null)
            foreach (var s in cloud.Record.Records) mapCloud[$"{s.Id}_{(int)s.Difficulty}"] = s;

        foreach (var d in diffs)
        {
            string key = $"{d.SongId}_{d.DifficultyIndex}";
            mapLocal.TryGetValue(key, out var l);
            mapCloud.TryGetValue(key, out var c);

            string suggestion = "equal";
            if (l != null && c != null)
            {
                if (l.Score > c.Score || (l.Score == c.Score && l.Accuracy > c.Accuracy)) suggestion = "local";
                else if (c.Score > l.Score || (c.Score == l.Score && c.Accuracy > l.Accuracy)) suggestion = "cloud";
                else suggestion = "equal";
            }
            else if (l != null) suggestion = "local";
            else if (c != null) suggestion = "cloud";

            list.Add(new DetailedScoreDiff(d.SongId, d.DifficultyIndex, d.LocalScore, d.LocalAcc, d.CloudScore, d.CloudAcc, suggestion));
        }

        return list;
    }

    /// <summary>
    /// 返回带本地/云对象引用的详细差异，方便上层 UI 构建展示数据。
    /// </summary>
    public async Task<List<DetailedScoreDiffWithObjects>> GetDetailedDiffsWithObjectsAsync(PhiSaveData? local, PhiSaveData? cloud)
    {
        var diffs = await GetDetailedDiffsAsync(local, cloud);
        var result = new List<DetailedScoreDiffWithObjects>();

        var mapLocal = new Dictionary<string, SongScore>();
        if (local?.Record?.Records != null)
            foreach (var s in local.Record.Records) mapLocal[$"{s.Id}_{(int)s.Difficulty}"] = s;

        var mapCloud = new Dictionary<string, SongScore>();
        if (cloud?.Record?.Records != null)
            foreach (var s in cloud.Record.Records) mapCloud[$"{s.Id}_{(int)s.Difficulty}"] = s;

        foreach (var d in diffs)
        {
            var key = $"{d.SongId}_{d.DifficultyIndex}";
            mapLocal.TryGetValue(key, out var l);
            mapCloud.TryGetValue(key, out var c);
            result.Add(new DetailedScoreDiffWithObjects(d, l, c));
        }

        return result;
    }

    /// <summary>
    /// 返回当前内存与云端的差异键列表（格式："{songId}_{difficultyIndex}"），用于 UI 验证与展示。
    /// </summary>
    public async Task<List<string>> GetCurrentDiffKeysAsync()
    {
        var local = CurrentSave;
        var (cloud, _, _) = await GetCloudSaveCopyAsync();
        var diffs = await DiffSavesAsync(local, cloud);
        return diffs.Select(d => $"{d.SongId}_{d.DifficultyIndex}").ToList();
    }

    /// <summary>
    /// 创建一个合并预览（不应用到 CurrentSave），并保存在 service 里以便后续 Apply/Discard
    /// 如果传入 local 或 cloud 为 null，则使用当前内存或云端副本
    /// </summary>
    public async Task<PhiSaveData?> CreateMergePreviewAsync(PhiSaveData? local = null, PhiSaveData? cloud = null)
    {
        if (local == null) local = CurrentSave;
        if (cloud == null)
        {
            var (cloudCopy, fileId, objId) = await GetCloudSaveCopyAsync();
            cloud = cloudCopy;
        }

        var merged = MergeSaves(local, cloud);
        _previewMerge = merged;
        return merged;
    }

    /// <summary>
    /// 应用先前创建的合并预览到 CurrentSave（不自动上传）
    /// </summary>
    public bool ApplyPreviewMerge()
    {
        if (_previewMerge == null) return false;
        CurrentSave = _previewMerge;
        _previewMerge = null;
        return true;
    }

    /// <summary>
    /// 丢弃当前保存的合并预览
    /// </summary>
    public bool DiscardPreviewMerge()
    {
        if (_previewMerge == null) return false;
        _previewMerge = null;
        return true;
    }

    /// <summary>
    /// 应用合并预览并上传到云端
    /// </summary>
    public async Task<bool> ApplyPreviewAndUploadAsync(string? oldFileId = null, string? oldObjId = null)
    {
        if (_previewMerge == null) return false;
        CurrentSave = _previewMerge;
        _previewMerge = null;
        await UploadSaveAsync(oldFileId, oldObjId);
        return true;
    }

    /// <summary>
    /// 应用合并预览，上传到云端，并可选择将合并结果保存到本地文件（localPath 可选）。
    /// </summary>
    public async Task<bool> ApplyPreviewAndUploadAsync(string? oldFileId = null, string? oldObjId = null, string? localPath = null)
    {
        if (_previewMerge == null) return false;
        CurrentSave = _previewMerge;
        _previewMerge = null;
        await UploadSaveAsync(oldFileId, oldObjId);
        if (!string.IsNullOrEmpty(localPath))
        {
            try
            {
                await SaveLocalToFileAsync(localPath);
            }
            catch { }
        }
        return true;
    }

    /// <summary>
    /// 获取当前合并预览（调试用）
    /// </summary>
    public PhiSaveData? GetPreviewMerge() => _previewMerge;

    /// <summary>
    /// 合并两个存档：按单曲分数取更高者，其他字段以最新非空为准
    /// </summary>
    public PhiSaveData MergeSaves(PhiSaveData? local, PhiSaveData? cloud)
    {
        var result = new PhiSaveData();

        var mergedRecords = new Dictionary<string, SongScore>();
        if (cloud?.Record?.Records != null)
        {
            foreach (var s in cloud.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                mergedRecords[key] = s;
            }
        }
        if (local?.Record?.Records != null)
        {
            foreach (var s in local.Record.Records)
            {
                var key = $"{s.Id}_{(int)s.Difficulty}";
                if (mergedRecords.TryGetValue(key, out var orig))
                {
                    if (s.Score > orig.Score || (s.Score == orig.Score && s.Accuracy > orig.Accuracy))
                        mergedRecords[key] = s;
                }
                else mergedRecords[key] = s;
            }
        }
        result.Record = new GameRecord(mergedRecords.Values.ToList(), 0);

        result.Progress = local?.Progress ?? cloud?.Progress;
        result.UserInfo = local?.UserInfo ?? cloud?.UserInfo;
        result.Settings = local?.Settings ?? cloud?.Settings;

        var keys = new Dictionary<string, GameKeyFlag>();
        if (cloud?.Keys != null)
            foreach (var kv in cloud.Keys) keys[kv.Key] = kv.Value;
        if (local?.Keys != null)
            foreach (var kv in local.Keys) keys[kv.Key] = kv.Value;
        result.Keys = keys;

        result.SummaryRks = Math.Max(local?.SummaryRks ?? 0f, cloud?.SummaryRks ?? 0f);

        return result;
    }
}
