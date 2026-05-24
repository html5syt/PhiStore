using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.Serialization;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
    /// <summary>
    /// 上传存档到云端
    /// </summary>
    public async Task UploadSaveAsync(string? oldFileId, string? oldObjId)
    {
        if (_saveObj == null || CurrentSave == null) throw new InvalidOperationException("Missing state");

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(_sessionToken))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }

        // 获取原有的 SaveInfo 以构建上下文
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        var originalInfo = container.Results.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(originalInfo?.User?.ObjectId))
        {
            _userObjectId = originalInfo.User.ObjectId;
        }

        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            throw new InvalidOperationException("Missing user object id for upload.");
        }

        var ctx = new SaveContext(new Dictionary<string, SaveContext.Entry>(), originalInfo!);
        CurrentSave.WriteToContext(ctx);

        // 构建 Summary：在打包前确保 ctx 中的 Summary 已更新为 CurrentSave 的值
        try
        {
            var existing = ctx.ReadSummary();
            if (existing != null)
            {
                existing.Rks = CurrentSave.SummaryRks;
                ctx.SaveSummary(existing);
            }
        }
        catch { }

        byte[] packedSave;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                if (_saveObj != null)
                {
                    await ctx.SaveToZipAsync(archive, async (b) => await _saveObj.Encrypt(b));
                }
                else
                {
                    await ctx.SaveToZipAsync(archive, async (b) => await Task.FromResult(b));
                }
            }
            packedSave = ms.ToArray();
        }

        byte[] packedSummary = Array.Empty<byte>();
        try
        {
            var ctxSummary = ctx.ReadSummary();
            if (ctxSummary != null)
            {
                using var ms2 = new MemoryStream();
                var bw = new ByteWriter(ms2);
                ctxSummary.Serialize(bw);
                packedSummary = ms2.ToArray();
            }
        }
        catch { }

        await UploadSaveAsync(oldFileId, oldObjId, packedSave, packedSummary);
    }

    public void Dispose()
    {
        _saveObj?.Dispose();
    }

    // ===== Local CRUD operations =====
    public Task<bool> SaveLocalToFileAsync(string path, byte[]? key = null, byte[]? iv = null)
    {
        if (CurrentSave == null) throw new InvalidOperationException("No save in memory to write.");
        var full = Path.GetFullPath(path);
        AESUtil.SaveEncrypted(full, CurrentSave, key, iv);
        return Task.FromResult(true);
    }

    public Task<PhiSaveData?> LoadLocalFromFileAsync(string path, byte[]? key = null, byte[]? iv = null)
    {
        var full = Path.GetFullPath(path);
        var loaded = AESUtil.LoadDecrypted(full, key, iv);
        if (loaded != null) CurrentSave = loaded;
        return Task.FromResult(loaded);
    }

    public Task<bool> DeleteLocalFileAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return Task.FromResult(false);
        File.Delete(full);
        return Task.FromResult(true);
    }

    public Task<List<string>> ListLocalSavesAsync(string directory, string searchPattern = "*")
    {
        var dir = Path.GetFullPath(directory);
        if (!Directory.Exists(dir)) return Task.FromResult(new List<string>());
        var files = Directory.GetFiles(dir, searchPattern).ToList();
        return Task.FromResult(files);
    }

    /// <summary>
    /// 获取云端存档的副本而不覆盖 CurrentSave
    /// 返回 (PhiSaveData, fileId, objId)
    /// </summary>
    public async Task<(PhiSaveData? CloudSave, string? FileId, string? ObjId)> GetCloudSaveCopyAsync()
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        if (container.Results.Count == 0) return (null, null, null);
        var targetIndex = -1;
        SaveContext? ctx = null;
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
            catch
            {
                // Skip broken save info entries.
            }
        }
        if (targetIndex < 0 || ctx == null) return (null, null, null);
        var info = container.Results[targetIndex];
        var copy = PhiSaveData.FromSaveContext(ctx);
        return (copy, info.GameFile?.ObjectId, info.ObjectId);
    }

    private async Task<DateTime?> FetchCloudModifiedUtcFromRawAsync()
    {
        if (_saveObj == null) return null;

        try
        {
            var baseUrl = !string.IsNullOrEmpty(_customCloudServer) ? _customCloudServer : Save.CloudServerAddress;
            var url = baseUrl.TrimEnd('/') + "/1.1/classes/_GameSave?limit=1";
            var resp = await _saveObj.Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var txt = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (!first.TryGetProperty("modifiedAt", out var modifiedAtElement)) return null;

            if (modifiedAtElement.ValueKind == JsonValueKind.String)
            {
                var s = modifiedAtElement.GetString();
                if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out var parsed)) return parsed.ToUniversalTime();
                return null;
            }

            if (modifiedAtElement.ValueKind == JsonValueKind.Object
                && modifiedAtElement.TryGetProperty("iso", out var isoElement)
                && isoElement.ValueKind == JsonValueKind.String)
            {
                var iso = isoElement.GetString();
                if (!string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, out var parsedIso)) return parsedIso.ToUniversalTime();
            }
        }
        catch
        {
            // Keep metadata retrieval best-effort.
        }

        return null;
    }

    // ===== Summary binary helpers =====
    /// <summary>
    /// 将当前内存中的 Summary 序列化为二进制（ByteWriter 输出）
    /// 返回 null 表示没有 Summary 可序列化
    /// </summary>
    public byte[]? SerializeCurrentSummaryToBytes()
    {
        var sum = CurrentSave?.GameSummary;
        if (sum == null) return null;
        using var ms = new MemoryStream();
        var bw = new ByteWriter(ms);
        sum.Serialize(bw);
        return ms.ToArray();
    }

    /// <summary>
    /// 从二进制数据解析 Summary 对象
    /// </summary>
    public PhigrosLibraryCSharp.CloudSave.Summary? DeserializeSummaryFromBytes(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        var br = new ByteReader(data);
        try
        {
            return Summary.FromReader(br);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将二进制 Summary 应用到当前内存（设置 CurrentSave.GameSummary 与 SummaryRks）
    /// </summary>
    public bool ApplySummaryBytesToCurrentSave(byte[] data)
    {
        var s = DeserializeSummaryFromBytes(data);
        if (s == null) return false;
        if (CurrentSave == null) CurrentSave = new PhiSaveData();
        CurrentSave.GameSummary = s;
        CurrentSave.SummaryRks = s.Rks;
        return true;
    }

    // ===== File IO helpers (engine-agnostic) =====
    public bool SaveSummaryToFile(string fullPath)
    {
        try
        {
            var bytes = SerializeCurrentSummaryToBytes();
            if (bytes == null || bytes.Length == 0) return false;
            System.IO.File.WriteAllBytes(fullPath, bytes);
            return true;
        }
        catch { return false; }
    }

    public bool LoadSummaryFromFile(string fullPath)
    {
        try
        {
            if (!System.IO.File.Exists(fullPath)) return false;
            var bytes = System.IO.File.ReadAllBytes(fullPath);
            return ApplySummaryBytesToCurrentSave(bytes);
        }
        catch { return false; }
    }

    public bool SaveJsonToFile(string fullPath)
    {
        try
        {
            if (CurrentSave == null) return false;
            var json = PhiSaveJsonCompat.Serialize(CurrentSave);
            System.IO.File.WriteAllText(fullPath, json);
            return true;
        }
        catch { return false; }
    }

    public bool LoadJsonFromFile(string fullPath)
    {
        try
        {
            if (!System.IO.File.Exists(fullPath)) return false;
            var content = System.IO.File.ReadAllText(fullPath);
            var imported = PhiSaveJsonCompat.Deserialize(content);
            if (imported != null)
            {
                CurrentSave = imported;
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public bool SaveJsonEncryptedToFile(string fullPath, byte[]? key = null, byte[]? iv = null)
    {
        try
        {
            if (CurrentSave == null) return false;
            AESUtil.SaveEncrypted(fullPath, CurrentSave, key, iv);
            return true;
        }
        catch { return false; }
    }

    public bool LoadJsonEncryptedFromFile(string fullPath, byte[]? key = null, byte[]? iv = null)
    {
        try
        {
            var loaded = AESUtil.LoadDecrypted(fullPath, key, iv);
            if (loaded != null)
            {
                CurrentSave = loaded;
                return true;
            }
            return false;
        }
        catch { return false; }
    }
}
