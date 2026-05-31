using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.Serialization;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;

// PhiSave2Service：提供本地文件存储、云端存储交互、Summary 处理、JSON 导入导出等功能的核心服务类
namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
    // ===== 本地与云端存档加载 =====
    /// <summary>
    /// 将当前内存中的存档以 AES（可选 key/iv）加密并写入本地文件。
    /// </summary>
    /// <param name="path">目标文件路径（相对或绝对）。</param>
    /// <param name="key">可选的 AES 密钥（若为空使用内置调试密钥）。</param>
    /// <param name="iv">可选的 AES IV（若为空使用内置调试 IV）。</param>
    /// <returns>返回表示写入是否成功的 <see cref="Task{Boolean}"/>。</returns>
    public Task<bool> SaveLocalToFileAsync(string path, byte[]? key = null, byte[]? iv = null)
    {
        if (CurrentSave == null) throw new InvalidOperationException("No save in memory to write.");
        var full = Path.GetFullPath(path);
        AESUtil.SaveEncrypted(full, CurrentSave, key, iv);
        return Task.FromResult(true);
    }

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
                // 跳过损坏的保存信息条目。
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
            // 保持元数据检索尽力而为。
        }

        return null;
    }

    // ===== Summary工具 =====
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

    // ===== JSON导入导出 =====
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
    /// 释放服务使用的底层资源，包括可能的云端 Save 对象连接。
    /// </summary>
    public void Dispose()
    {
        _saveObj?.Dispose();
    }
}
