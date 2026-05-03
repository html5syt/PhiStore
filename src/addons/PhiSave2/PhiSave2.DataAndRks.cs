#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PhigrosLibraryCSharp.GameRecords;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2
{
    /// <summary>
    /// 导出当前解密后条目为可序列化的明文结构（entries: base64, headers, summaryBase64）。
    /// </summary>
    /// <returns>包含 base64 entries、headers 以及 summaryBase64 的 Godot Dictionary。</returns>
    public Godot.Variant ExportPlainData()
    {
        var entries = _decryptedEntries.ToDictionary(
            kvp => kvp.Key,
            kvp => Convert.ToBase64String(kvp.Value));
        var headers = _entryHeaders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var payload = new
        {
            entries,
            headers,
            summaryBase64 = _rawSummary.Length == 0 ? string.Empty : Convert.ToBase64String(_rawSummary)
        };
        return ToGodotVariant(payload);
    }

    /// <param name="data">由 <see cref="ExportPlainData"/> 产生的明文数据结构。</param>
    public void ImportPlainData(Godot.Collections.Dictionary data)
    {
        /// <summary>
        /// 从明文结构恢复到内部解密条目（覆盖当前缓存的数据）。
        /// 传入结构应匹配 ExportPlainData 的输出。
        /// </summary>
        if (!data.ContainsKey("entries"))
        {
            throw new ArgumentException("Missing entries in payload.");
        }

        _decryptedEntries.Clear();
        var entries = (Godot.Collections.Dictionary)data["entries"];
        foreach (var entry in entries)
        {
            string key = entry.Key.ToString() ?? string.Empty;
            string value = entry.Value.ToString() ?? string.Empty;
            _decryptedEntries[key] = Convert.FromBase64String(value);
        }

        if (data.ContainsKey("summaryBase64"))
        {
            string summaryBase64 = data["summaryBase64"].ToString() ?? string.Empty;
            _rawSummary = string.IsNullOrEmpty(summaryBase64) ? Array.Empty<byte>() : Convert.FromBase64String(summaryBase64);
        }

        if (data.ContainsKey("headers"))
        {
            _entryHeaders.Clear();
            var headers = (Godot.Collections.Dictionary)data["headers"];
            foreach (var header in headers)
            {
                string key = header.Key.ToString() ?? string.Empty;
                _entryHeaders[key] = Convert.ToByte(header.Value, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// 将当前存档解析为可读的结构（Summary、Record、Settings、Progress、UserInfo），方便在 GDScript 层展示。
    /// </summary>
    /// <param name="constantsMap">可选的定数映射，用以还原谱面相关常数。</param>
    /// <returns>Godot Dictionary 格式的可读数据。</returns>
    public Godot.Variant ExportReadableData(Godot.Collections.Dictionary constantsMap)
    {
        if (_decryptedEntries.Count == 0)
        {
            return default;
        }

        var difficulties = ParseConstantsMap(constantsMap);
        GameRecord record = ReadGameRecord(difficulties);
        Summary? summary = _context?.ReadSummary();
        GameSettings? settings = ReadGameSettings();
        GameProgress? progress = ReadGameProgress();
        GameUserInfo? userInfo = ReadGameUserInfo();

        var payload = new
        {
            Summary = summary,
            Record = record,
            Settings = settings,
            Progress = progress,
            UserInfo = userInfo
        };
        return ToGodotVariant(payload);
    }

    /// <summary>
    /// 基于传入的定数表计算 RKS，返回 Godot 可直接消费的结构（TotalRks、Phi、B19）。
    /// </summary>
    /// <param name="constantsMap">谱面定数映射，键为 sid，值为 float 数组。</param>
    /// <returns>包含 RKS 结果的 Godot Dictionary。</returns>
    public Godot.Variant CalculateRks(Godot.Collections.Dictionary constantsMap)
    {
        if (_decryptedEntries.Count == 0)
        {
            return default;
        }

        var difficulties = ParseConstantsMap(constantsMap);
        GameRecord record = ReadGameRecord(difficulties);
        return BuildRksResult(record);
    }

    /// <summary>
    /// 返回 summary 与基于指定定数表计算出的 RKS 结果的组合结构。
    /// </summary>
    /// <param name="constantsMap">谱面定数映射。</param>
    /// <returns>包含 `Summary` 和 `Rks` 的 Godot Dictionary。</returns>
    public Godot.Variant GetSummaryWithRks(Godot.Collections.Dictionary constantsMap)
    {
        if (_decryptedEntries.Count == 0)
        {
            return default;
        }

        var difficulties = ParseConstantsMap(constantsMap);
        GameRecord record = ReadGameRecord(difficulties);
        Summary? summary = _context?.ReadSummary();
        var rksResult = BuildRksResult(record);

        var payload = new
        {
            Summary = summary,
            Rks = rksResult
        };
        return ToGodotVariant(payload);
    }

    private static Dictionary<string, float[]> ParseConstantsMap(Godot.Collections.Dictionary constantsMap)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var entry in constantsMap)
        {
            string key = entry.Key.ToString() ?? string.Empty;
            float[] constants = ParseConstantArray(entry.Value);
            result[key] = constants;
        }
        return result;
    }

    private static float[] ParseConstantArray(object value)
    {
        if (value is Godot.Collections.Array array)
        {
            float[] result = new float[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                result[i] = Convert.ToSingle(array[i], CultureInfo.InvariantCulture);
            }
            return result;
        }

        if (value is float[] floats)
        {
            return floats.ToArray();
        }

        if (value is double[] doubles)
        {
            return doubles.Select(x => (float)x).ToArray();
        }

        if (value is IEnumerable<float> floatEnumerable)
        {
            return floatEnumerable.ToArray();
        }

        if (value is IEnumerable<double> doubleEnumerable)
        {
            return doubleEnumerable.Select(x => (float)x).ToArray();
        }

        throw new ArgumentException("Invalid constants map value.");
    }

    private Godot.Variant BuildRksResult(GameRecord record)
    {
        var (phis, others, rks) = record.GetSortedListForRks();
        var phiList = phis.Select(BuildScoreInfo).ToArray();
        var b19List = others.Take(19).Select(BuildScoreInfo).ToArray();

        var payload = new
        {
            TotalRks = rks,
            Phi = phiList,
            B19 = b19List
        };
        return ToGodotVariant(payload);
    }

    private static object BuildScoreInfo(CompleteScore score)
    {
        return new
        {
            Sid = score.Id,
            Diff = score.Difficulty.ToString(),
            Score = score.Score,
            Acc = score.Accuracy,
            Rks = score.Rks
        };
    }
}
