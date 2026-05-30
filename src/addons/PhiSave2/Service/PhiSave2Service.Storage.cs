using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
                SaveContext.CipherFunction encryptor = (data, ct) =>
                {
                    if (_saveObj == null) return Task.FromResult(data);
                    return _saveObj.Encrypt(data, ct);
                };
                await ctx.SaveToZipAsync(archive, encryptor, CancellationToken.None);
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

    /// <summary>
    /// 释放服务使用的底层资源，包括可能的云端 Save 对象连接。
    /// </summary>
    /// <summary>
    /// 释放 Service 持有的底层资源（如 SaveObject 客户端）。
    /// </summary>
    public void Dispose()
    {
        _saveObj?.Dispose();
    }

    // ===== Local CRUD operations =====
    /// <summary>
    /// 将当前内存中的存档以 AES（可选 key/iv）加密并写入本地文件。
    /// </summary>
    /// <param name="path">目标文件路径（相对或绝对）。</param>
    /// <param name="key">可选的 AES 密钥（若为空使用内置调试密钥）。</param>
    /// <param name="iv">可选的 AES IV（若为空使用内置调试 IV）。</param>
    /// <returns>返回表示写入是否成功的 <see cref="Task{Boolean}"/>。</returns>
    /// <summary>
    /// 将当前内存中的存档以加密格式保存到指定本地路径。
    /// </summary>
    /// <param name="path">目标文件路径，支持相对或绝对路径。</param>
    /// <param name="key">可选 AES 密钥（16/24/32 字节），为空时使用内部调试密钥。</param>
    /// <param name="iv">可选 AES 初始向量（16 字节），为空时使用内部调试 IV。</param>
    /// <returns>异步任务，完成时返回是否写入成功。</returns>
    public Task<bool> SaveLocalToFileAsync(string path, byte[]? key = null, byte[]? iv = null)
    {
        if (CurrentSave == null) throw new InvalidOperationException("No save in memory to write.");
        var full = Path.GetFullPath(path);
        AESUtil.SaveEncrypted(full, CurrentSave, key, iv);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 从本地 AES 加密文件加载存档并设置为 <see cref="CurrentSave"/>。
    /// </summary>
    /// <param name="path">要加载的文件路径。</param>
    /// <param name="key">可选的 AES 密钥（与保存时一致）。</param>
    /// <param name="iv">可选的 AES IV（与保存时一致）。</param>
    /// <returns>若加载成功返回解析后的 <see cref="PhiSaveData"/>，否则返回 null。</returns>
    /// <summary>
    /// 从本地加密文件加载存档并设置为当前内存存档（CurrentSave）。
    /// </summary>
    /// <param name="path">源文件路径，支持相对或绝对路径。</param>
    /// <param name="key">可选 AES 密钥，需与保存时一致以解密成功。</param>
    /// <param name="iv">可选 AES 初始向量，需与保存时一致以解密成功。</param>
    /// <returns>异步任务，完成时返回解析出的 <see cref="PhiSaveData"/>；失败返回 <c>null</c>。</returns>
    public Task<PhiSaveData?> LoadLocalFromFileAsync(string path, byte[]? key = null, byte[]? iv = null)
    {
        var full = Path.GetFullPath(path);
        var loaded = AESUtil.LoadDecrypted(full, key, iv);
        if (loaded != null) CurrentSave = loaded;
        return Task.FromResult(loaded);
    }

    /// <summary>
    /// 删除指定的本地存档文件。
    /// </summary>
    /// <param name="path">要删除的文件路径。</param>
    /// <returns>删除成功返回 true；文件不存在或出错返回 false。</returns>
    /// <summary>
    /// 删除指定的本地存档文件。
    /// </summary>
    /// <param name="path">要删除的文件路径。</param>
    /// <returns>异步任务，完成时返回是否成功删除文件。</returns>
    public Task<bool> DeleteLocalFileAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return Task.FromResult(false);
        File.Delete(full);
        return Task.FromResult(true);
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
    /// <summary>
    /// 将当前存档的 Summary 序列化为二进制字节数组，适合持久化或网络传输。
    /// 返回 null 表示当前没有 Summary 可序列化。
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
    /// <summary>
    /// 从二进制字节数组解析出 <see cref="Summary"/> 对象。
    /// 若解析失败或数据为空则返回 null。
    /// </summary>
    /// <param name="data">包含 Summary 二进制数据的字节数组。</param>
    public Summary? DeserializeSummaryFromBytes(byte[] data)
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
    /// <summary>
    /// 将二进制 Summary 应用到当前内存存档（设置 GameSummary 与 SummaryRks）。
    /// </summary>
    /// <param name="data">Summary 的二进制表示。</param>
    /// <returns>应用成功返回 true，否则返回 false。</returns>
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
    /// <summary>
    /// 将当前 Summary 的二进制写入到指定文件。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <returns>写入成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 将当前 Summary 二进制序列化结果写入文件（原始二进制，不压缩）。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <returns>写入成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool SaveSummaryToFile(string fullPath)
    {
        try
        {
            var bytes = SerializeCurrentSummaryToBytes();
            if (bytes == null || bytes.Length == 0) return false;
            File.WriteAllBytes(fullPath, bytes);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 从文件加载 Summary 二进制并应用到当前内存存档。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <returns>加载并应用成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 从文件读取 Summary 二进制并应用到当前内存存档。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <returns>成功解析并应用返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool LoadSummaryFromFile(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return false;
            var bytes = File.ReadAllBytes(fullPath);
            return ApplySummaryBytesToCurrentSave(bytes);
        }
        catch { return false; }
    }

    /// <summary>
    /// 将当前存档导出为 JSON 文本并写入指定文件。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <returns>写入成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 将当前内存的存档导出为可读 JSON 并写入文件。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <returns>写入成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool SaveJsonToFile(string fullPath)
    {
        try
        {
            if (CurrentSave == null) return false;
            var json = PhiSaveJsonCompat.Serialize(CurrentSave);
            File.WriteAllText(fullPath, json);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 从 JSON 文本文件加载存档并设置为 <see cref="CurrentSave"/>。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <returns>加载成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 从可读 JSON 文件导入存档并设置为当前内存存档。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <returns>成功解析并应用返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool LoadJsonFromFile(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return false;
            var content = File.ReadAllText(fullPath);
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

    /// <summary>
    /// 将当前存档导出为 JSON 并以 AES 加密写入文件。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <param name="key">可选 AES 密钥。</param>
    /// <param name="iv">可选 AES IV。</param>
    /// <returns>写入成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 将当前内存存档序列化为 JSON 并以 AES 加密后写入文件。
    /// </summary>
    /// <param name="fullPath">目标文件完整路径。</param>
    /// <param name="key">可选 AES 密钥，需与解密时一致。</param>
    /// <param name="iv">可选 AES 初始向量，需与解密时一致。</param>
    /// <returns>写入成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
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

    /// <summary>
    /// 从 AES 加密的 JSON 文件加载存档并设置为 <see cref="CurrentSave"/>。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <param name="key">可选 AES 密钥（与保存时一致）。</param>
    /// <param name="iv">可选 AES IV（与保存时一致）。</param>
    /// <returns>加载成功返回 true，否则返回 false。</returns>
    /// <summary>
    /// 从 AES 加密的 JSON 文件解密并导入存档到内存。
    /// </summary>
    /// <param name="fullPath">源文件完整路径。</param>
    /// <param name="key">用于解密的 AES 密钥。</param>
    /// <param name="iv">用于解密的 AES 初始向量。</param>
    /// <returns>成功解密并导入返回 <c>true</c>，否则返回 <c>false</c>。</returns>
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
