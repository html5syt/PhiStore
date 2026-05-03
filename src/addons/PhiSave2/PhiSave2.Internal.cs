#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using PhigrosLibraryCSharp;
using PhigrosLibraryCSharp.Extensions;
using PhigrosLibraryCSharp.GameRecords;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2
{
    // 内部方法：将 SaveContext 缓存到实例字段，用于后续读取/打包/计算。
    private void CacheContext(SaveContext ctx)
    {
        _context = ctx;
        _rawZip = ctx.RawZip;
        _rawSummary = ctx.RawSummary;
        _rawEntries.Clear();
        _decryptedEntries.Clear();
        _entryHeaders.Clear();

        foreach (var kvp in ctx.RawDataEntries)
        {
            _rawEntries[kvp.Key] = kvp.Value;
            _entryHeaders[kvp.Key] = kvp.Value.Length > 0 ? kvp.Value[0] : (byte)0;
        }
        foreach (var kvp in ctx.DecryptedDataEntries)
        {
            _decryptedEntries[kvp.Key] = kvp.Value;
        }
    }

    // 从云端原始查询结果解析指定索引的存档条目信息（用于上传/更新逻辑）。
    private static async Task<CloudSaveEntry> FetchCloudEntryAsync(int index, Save save)
    {
        JsonNode root = await PhiSaveUploader.FetchRawSaveAsNode(save);
        JsonArray results = root["results"]?.AsArray() ?? throw new InvalidOperationException("Missing results.");
        if (index < 0 || index >= results.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        JsonObject item = results[index]?.AsObject() ?? throw new InvalidOperationException("Invalid save entry.");
        JsonObject user = item["user"]?.AsObject() ?? throw new InvalidOperationException("Missing user info.");
        JsonObject gameFile = item["gameFile"]?.AsObject() ?? throw new InvalidOperationException("Missing game file info.");

        return new CloudSaveEntry
        {
            UserObjectId = user["objectId"]?.GetValue<string>() ?? string.Empty,
            SaveObjectId = item["objectId"]?.GetValue<string>() ?? string.Empty,
            GameFileObjectId = gameFile["objectId"]?.GetValue<string>(),
            GameFileUrl = gameFile["url"]?.GetValue<string>() ?? string.Empty,
            SummaryBase64 = item["summary"]?.GetValue<string>() ?? string.Empty
        };
    }

    // 从当前缓存或 Raw 条目读取并解析 GameRecord。
    private GameRecord ReadGameRecord(IReadOnlyDictionary<string, float[]> difficulties)
    {
        if (_context != null)
        {
            return _context.ReadGameRecord(difficulties);
        }

        if (!_decryptedEntries.TryGetValue("gameRecord", out byte[]? data))
        {
            throw new InvalidOperationException("gameRecord entry missing.");
        }

        byte version = _entryHeaders.TryGetValue("gameRecord", out byte header) ? header : (byte)0;
        ByteReader reader = new(data, 0, version);
        return new GameRecord
        {
            Version = reader.ObjectVersion,
            Records = reader.ReadAllGameRecord(difficulties, null),
            Summary = _rawSummary.Length == 0 ? string.Empty : Convert.ToBase64String(_rawSummary)
        };
    }

    // 读取游戏设置结构（可为 null）。
    private GameSettings? ReadGameSettings()
    {
        if (_context != null)
        {
            return _context.ReadGameSettings();
        }

        if (!_decryptedEntries.TryGetValue("settings", out byte[]? data))
        {
            return null;
        }

        byte version = _entryHeaders.TryGetValue("settings", out byte header) ? header : (byte)0;
        ByteReader reader = new(data, 0, version);
        return reader.ReadGameSettings();
    }

    // 读取游戏进度结构（可为 null）。
    private GameProgress? ReadGameProgress()
    {
        if (_context != null)
        {
            return _context.ReadGameProgress();
        }

        if (!_decryptedEntries.TryGetValue("gameProgress", out byte[]? data))
        {
            return null;
        }

        byte version = _entryHeaders.TryGetValue("gameProgress", out byte header) ? header : (byte)0;
        ByteReader reader = new(data, 0, version);
        return reader.ReadGameProgress();
    }

    // 读取用户信息结构（可为 null）。
    private GameUserInfo? ReadGameUserInfo()
    {
        if (_context != null)
        {
            return _context.ReadGameUserInfo();
        }

        if (!_decryptedEntries.TryGetValue("user", out byte[]? data))
        {
            return null;
        }

        byte version = _entryHeaders.TryGetValue("user", out byte header) ? header : (byte)0;
        ByteReader reader = new(data, 0, version);
        return reader.ReadGameUserInfo();
    }

    // 将当前内部解密条目重新加密并打包为 ZIP 字节数组。
    private byte[] BuildZip()
    {
        var rawEntries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var kvp in _decryptedEntries)
        {
            byte header = _entryHeaders.TryGetValue(kvp.Key, out byte value) ? value : (byte)0;
            byte[] encrypted = EncryptLocal(kvp.Value);
            byte[] packed = new byte[encrypted.Length + 1];
            packed[0] = header;
            Buffer.BlockCopy(encrypted, 0, packed, 1, encrypted.Length);
            rawEntries[kvp.Key] = packed;
        }

        using MemoryStream ms = new();
        using (ZipArchive archive = new(ms, ZipArchiveMode.Create, true))
        {
            foreach (var kvp in rawEntries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(kvp.Key);
                using Stream stream = entry.Open();
                stream.Write(kvp.Value, 0, kvp.Value.Length);
            }
        }

        return ms.ToArray();
    }

    // 本地加密（用于 game file 条目写回）。
    private static byte[] EncryptLocal(byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIv;
        aes.Padding = PaddingMode.PKCS7;

        using MemoryStream ms = new();
        using CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();
        return ms.ToArray();
    }

    // 本地解密（用于读取 game file 条目）。
    private static byte[] DecryptLocal(byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIv;
        aes.Padding = PaddingMode.PKCS7;

        using MemoryStream ms = new();
        using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Write);
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();
        return ms.ToArray();
    }

    // 把任意对象 JSON 序列化后再交给 Godot 解析成 Variant（便于 GDScript 侧直接使用）。
    private static Godot.Variant ToGodotVariant(object obj)
    {
#pragma warning disable IL2026, IL3050
        string json = JsonSerializer.Serialize(obj, JsonOptions);
#pragma warning restore IL2026, IL3050
        return Godot.Json.ParseString(json);
    }
}
