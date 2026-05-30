using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PhigrosLibraryCSharp.CloudSave;
using PhiStore.Addons.PhiSave2.Internal;
using PhiStore.Addons.PhiSave2.Models;

namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{
    private static readonly Dictionary<string, string> MemoryEntryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gameRecord"] = "gameRecord",
        ["record"] = "gameRecord",
        ["gameProgress"] = "gameProgress",
        ["progress"] = "gameProgress",
        ["gameUserInfo"] = "gameUserInfo",
        ["userInfo"] = "gameUserInfo",
        ["gameSettings"] = "gameSettings",
        ["settings"] = "gameSettings",
        ["gameKeys"] = "gameKeys",
        ["keys"] = "gameKeys"
    };

    /// <summary>
    /// 获取当前内存存档中指定 entry 的字典快照。
    /// 支持 gameRecord、gameProgress、gameUserInfo、gameSettings、gameKeys 及常用别名。
    /// </summary>
    /// <param name="entryName">条目名称。</param>
    /// <returns>返回指定 entry 的字典快照；如果当前没有存档或条目为空则返回 null。</returns>
    public Dictionary<string, object?>? GetMemoryEntry(string entryName)
    {
        var entryKey = NormalizeMemoryEntryName(entryName);
        if (entryKey == null || CurrentSave == null) return null;

        string? json = entryKey switch
        {
            "gameRecord" => CurrentSave.Record == null ? null : JsonSerializer.Serialize(CurrentSave.Record, PhiSaveJsonContext.Default.GameRecord),
            "gameProgress" => CurrentSave.Progress == null ? null : JsonSerializer.Serialize(CurrentSave.Progress, PhiSaveJsonContext.Default.GameProgress),
            "gameUserInfo" => CurrentSave.UserInfo == null ? null : JsonSerializer.Serialize(CurrentSave.UserInfo, PhiSaveJsonContext.Default.GameUserInfo),
            "gameSettings" => CurrentSave.Settings == null ? null : JsonSerializer.Serialize(CurrentSave.Settings, PhiSaveJsonContext.Default.GameSettings),
            "gameKeys" => JsonSerializer.Serialize(CurrentSave.Keys, PhiSaveJsonContext.Default.DictionaryStringGameKeyFlag),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? JsonElementToDictionary(doc.RootElement)
            : new Dictionary<string, object?>();
    }

    /// <summary>
    /// 将字典写回当前内存存档中的指定 entry。
    /// </summary>
    /// <param name="entryName">条目名称。</param>
    /// <param name="entryData">要写入的字典内容。</param>
    /// <returns>写入成功返回 true；条目名称无效或解析失败返回 false。</returns>
    public bool SetMemoryEntry(string entryName, Dictionary<string, object?> entryData)
    {
        var entryKey = NormalizeMemoryEntryName(entryName);
        if (entryKey == null) return false;

        CurrentSave ??= new PhiSaveData();

        try
        {
            var json = SerializePlainObject(entryData);
            switch (entryKey)
            {
                case "gameRecord":
                    CurrentSave.Record = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.GameRecord);
                    if (CurrentSave.Record == null) return false;
                    break;
                case "gameProgress":
                    CurrentSave.Progress = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.GameProgress);
                    if (CurrentSave.Progress == null) return false;
                    break;
                case "gameUserInfo":
                    CurrentSave.UserInfo = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.GameUserInfo);
                    if (CurrentSave.UserInfo == null) return false;
                    break;
                case "gameSettings":
                    CurrentSave.Settings = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.GameSettings);
                    if (CurrentSave.Settings == null) return false;
                    break;
                case "gameKeys":
                    CurrentSave.Keys = JsonSerializer.Deserialize(json, PhiSaveJsonContext.Default.DictionaryStringGameKeyFlag) ?? new Dictionary<string, GameKeyFlag>();
                    break;
                default:
                    return false;
            }

            CurrentSave.ModifiedAt = DateTime.UtcNow;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeMemoryEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        return MemoryEntryAliases.TryGetValue(entryName.Trim(), out var canonical) ? canonical : null;
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonElementToObject(property.Value);
        }
        return result;
    }

    private static List<object?> JsonElementToList(JsonElement element)
    {
        var result = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            result.Add(JsonElementToObject(item));
        }
        return result;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => JsonElementToList(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string SerializePlainObject(Dictionary<string, object?> entryData)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WritePlainDictionary(writer, entryData);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePlainDictionary(Utf8JsonWriter writer, Dictionary<string, object?> dictionary)
    {
        writer.WriteStartObject();
        foreach (var kv in dictionary)
        {
            writer.WritePropertyName(kv.Key);
            WritePlainValue(writer, kv.Value);
        }
        writer.WriteEndObject();
    }

    private static void WritePlainArray(Utf8JsonWriter writer, System.Collections.IEnumerable values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            WritePlainValue(writer, value);
        }
        writer.WriteEndArray();
    }

    private static void WritePlainValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                break;
            case sbyte sbyteValue:
                writer.WriteNumberValue(sbyteValue);
                break;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                break;
            case ushort ushortValue:
                writer.WriteNumberValue(ushortValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                break;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            case Dictionary<string, object?> dictionary:
                WritePlainDictionary(writer, dictionary);
                break;
            case System.Collections.IEnumerable enumerable when value is not string:
                WritePlainArray(writer, enumerable);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}