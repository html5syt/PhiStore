using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PhiStore.Addons.PhiSave2.Models;
using PhigrosLibraryCSharp.CloudSave;

namespace PhiStore.Addons.PhiSave2.Internal;

/// <summary>
/// PhiSave2 的 JSON 兼容层。
/// 仅使用 AOT 安全的、明确类型化的转换器；GameKeyFlag 不输出可计算字段。
/// </summary>
public static class PhiSaveJsonCompat
{
    // 共享的 JsonSerializerContext 实例，使用预定义选项和显式转换器。
    private static readonly PhiSaveJsonContext SharedContext = new PhiSaveJsonContext(CreateOptions());

    /// <summary>
    /// 公开当前兼容层使用的 JSON 选项，供同一套转换器在其它 entry 级读写场景中复用。
    /// </summary>
    public static JsonSerializerOptions Options => SharedContext.Options;

    // 提供直接的序列化/反序列化方法，供外部调用。
    public static string Serialize(PhiSaveData data)
    {
        var json = JsonSerializer.Serialize(data, SharedContext.PhiSaveData);
        return UnescapeUnicodeEscapes(json);
    }

    public static PhiSaveData? Deserialize(string json)
    {
        var normalized = NormalizeInputJson(json);
        return (PhiSaveData?)JsonSerializer.Deserialize(normalized, SharedContext.PhiSaveData);
    }

#pragma warning disable IL2026, IL3050
    /// <summary>
    /// 使用当前兼容层的选项序列化任意支持的值。
    /// </summary>
    public static string SerializeValue<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// 使用当前兼容层的选项反序列化任意支持的值。
    /// </summary>
    public static T? DeserializeValue<T>(string json)
    {
        var normalized = NormalizeInputJson(json);
        return JsonSerializer.Deserialize<T>(normalized, Options);
    }
#pragma warning restore IL2026, IL3050

    // 将 Unicode 转义序列转换回原始字符
    public static string NormalizeInputJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return UnescapeUnicodeEscapes(raw);
    }

    // 将 Unicode 转义序列（\uXXXX）转换回原始字符，避免因转义导致的解析问题。
    private static string UnescapeUnicodeEscapes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, "\\\\u([0-9a-fA-F]{4})", static m =>
        {
            var code = Convert.ToInt32(m.Groups[1].Value, 16);
            return ((char)code).ToString();
        });
    }

    // AES 加密/解密相关的兼容方法，保持与原有实现一致的加密流程（AES + GZip），并提供默认密钥/IV 以确保兼容性。
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        // Keep explicit converters for backward-compatibility; source-gen TypeInfoResolver is used for AOT.
        options.Converters.Add(new SongScoreJsonConverter());
        options.Converters.Add(new GameRecordJsonConverter());
        options.Converters.Add(new GameSettingsJsonConverter());
        options.Converters.Add(new GameProgressJsonConverter());
        options.Converters.Add(new GameKeyFlagJsonConverter());
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new GameProgressNodeVersion2JsonConverter());
        options.Converters.Add(new GameProgressNodeVersion3JsonConverter());
        options.Converters.Add(new GameProgressNodeVersion4JsonConverter());
        options.Converters.Add(new GameUserInfoJsonConverter());
        return options;
    }

    // 5个主要Entries的转换器
    private sealed class SongScoreJsonConverter : JsonConverter<SongScore>
    {
        public override SongScore Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            var score = GetInt(root, "Score", 0);
            var acc = GetFloat(root, "Accuracy", GetFloat(root, "Acc", 0f));
            var id = GetString(root, "Id", string.Empty);
            var difficulty = ParseEnum(root, "Difficulty", (Difficulty)0);
            var status = ParseScoreStatus(root);
            return new SongScore(score, acc, id, difficulty, status);
        }

        public override void Write(Utf8JsonWriter writer, SongScore value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Score");
            writer.WriteNumberValue(value.Score);
            writer.WritePropertyName("Accuracy");
            writer.WriteNumberValue(value.Accuracy);
            writer.WritePropertyName("Id");
            writer.WriteStringValue(value.Id);
            writer.WritePropertyName("Difficulty");
            writer.WriteStringValue(value.Difficulty.ToString());
            writer.WritePropertyName("Status");
            writer.WriteStringValue(value.Status.ToString());
            writer.WriteEndObject();
        }
    }

    private sealed class GameRecordJsonConverter : JsonConverter<GameRecord>
    {
        public override GameRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            var version = (byte)GetInt(root, "Version", 0);
            var records = new List<SongScore>();
            if (TryGetProperty(root, "Records", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var score = ParseSongScore(item);
                    if (score != null) records.Add(score);
                }
            }

            return new GameRecord(records, version);
        }

        public override void Write(Utf8JsonWriter writer, GameRecord value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Version");
            writer.WriteNumberValue(value.Version);
            writer.WritePropertyName("Records");
            writer.WriteStartArray();
            foreach (var score in value.Records)
            {
                WriteSongScore(writer, score);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static SongScore? ParseSongScore(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;

            var score = GetInt(root, "Score", 0);
            var acc = GetFloat(root, "Accuracy", GetFloat(root, "Acc", 0f));
            var id = GetString(root, "Id", string.Empty);
            var difficulty = ParseEnum(root, "Difficulty", (Difficulty)0);
            var status = ParseScoreStatus(root);
            return new SongScore(score, acc, id, difficulty, status);
        }

        private static void WriteSongScore(Utf8JsonWriter writer, SongScore value)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Score", value.Score);
            writer.WriteNumber("Accuracy", value.Accuracy);
            writer.WriteString("Id", value.Id);
            writer.WriteString("Difficulty", value.Difficulty.ToString());
            writer.WriteString("Status", value.Status.ToString());
            writer.WriteEndObject();
        }
    }

    private static ScoreStatus ParseScoreStatus(JsonElement root)
    {
        if (TryGetProperty(root, "Status", out var statusNode))
        {
            if (statusNode.ValueKind == JsonValueKind.String)
            {
                var raw = statusNode.GetString();
                if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<ScoreStatus>(raw, true, out var parsed))
                {
                    return parsed;
                }
            }
            else if (statusNode.ValueKind == JsonValueKind.Number)
            {
                return ParseEnum(root, "Status", (ScoreStatus)0);
            }
        }

        return ScoreStatus.NotFc;
    }

    private sealed class GameSettingsJsonConverter : JsonConverter<GameSettings>
    {
        public override GameSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            return new GameSettings(
                (byte)GetInt(root, "Version", 0),
                GetBool(root, "ChordSupport", false),
                GetBool(root, "FcApIndicatorOn", false),
                GetBool(root, "EnableHitSound", false),
                GetBool(root, "LowResolutionModeOn", false),
                GetString(root, "DeviceName", string.Empty),
                GetFloat(root, "BackgroundBrightness", 1f),
                GetFloat(root, "MusicVolume", 1f),
                GetFloat(root, "EffectVolume", 1f),
                GetFloat(root, "HitSoundVolume", 1f),
                GetFloat(root, "SoundOffset", 0f),
                GetFloat(root, "NoteScale", 1f)
            );
        }

        public override void Write(Utf8JsonWriter writer, GameSettings value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Version");
            writer.WriteNumberValue(value.Version);
            writer.WritePropertyName("ChordSupport");
            writer.WriteBooleanValue(value.ChordSupport);
            writer.WritePropertyName("FcApIndicatorOn");
            writer.WriteBooleanValue(value.FcApIndicatorOn);
            writer.WritePropertyName("EnableHitSound");
            writer.WriteBooleanValue(value.EnableHitSound);
            writer.WritePropertyName("LowResolutionModeOn");
            writer.WriteBooleanValue(value.LowResolutionModeOn);
            writer.WritePropertyName("DeviceName");
            writer.WriteStringValue(value.DeviceName);
            writer.WritePropertyName("BackgroundBrightness");
            writer.WriteNumberValue(value.BackgroundBrightness);
            writer.WritePropertyName("MusicVolume");
            writer.WriteNumberValue(value.MusicVolume);
            writer.WritePropertyName("EffectVolume");
            writer.WriteNumberValue(value.EffectVolume);
            writer.WritePropertyName("HitSoundVolume");
            writer.WriteNumberValue(value.HitSoundVolume);
            writer.WritePropertyName("SoundOffset");
            writer.WriteNumberValue(value.SoundOffset);
            writer.WritePropertyName("NoteScale");
            writer.WriteNumberValue(value.NoteScale);
            writer.WriteEndObject();
        }
    }

    private sealed class GameProgressJsonConverter : JsonConverter<GameProgress>
    {
        public override GameProgress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            var challenge = ParseChallenge(root);
            var money = ParseMoney(root);
            var node2 = ParseNode2(root);

            return new GameProgress(
                (byte)GetInt(root, "Version", 0),
                GetBool(root, "IsFirstRun", true),
                GetBool(root, "LegacyChapterFinished", false),
                GetBool(root, "AlreadyShowCollectionTip", false),
                GetBool(root, "AlreadyShowAutoUnlockINTip", false),
                GetString(root, "GameCompleted", string.Empty),
                (short)GetInt(root, "SongUpdateInfo", 0),
                challenge,
                money,
                ParseEnum(root, "UnlockFlagOfSpasmodic", (DifficultyUnlockFlag)0),
                ParseEnum(root, "UnlockFlagOfIgallta", (DifficultyUnlockFlag)0),
                ParseEnum(root, "UnlockFlagOfRrharil", (DifficultyUnlockFlag)0),
                ParseEnum(root, "FlagOfSongRecordKey", (SongRecordFlag)0),
                node2
            );
        }

        public override void Write(Utf8JsonWriter writer, GameProgress value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Version");
            writer.WriteNumberValue(value.Version);
            writer.WritePropertyName("IsFirstRun");
            writer.WriteBooleanValue(value.IsFirstRun);
            writer.WritePropertyName("LegacyChapterFinished");
            writer.WriteBooleanValue(value.LegacyChapterFinished);
            writer.WritePropertyName("AlreadyShowCollectionTip");
            writer.WriteBooleanValue(value.AlreadyShowCollectionTip);
            writer.WritePropertyName("AlreadyShowAutoUnlockINTip");
            writer.WriteBooleanValue(value.AlreadyShowAutoUnlockINTip);
            writer.WritePropertyName("GameCompleted");
            writer.WriteStringValue(value.GameCompleted);
            writer.WritePropertyName("SongUpdateInfo");
            writer.WriteNumberValue(value.SongUpdateInfo);
            writer.WritePropertyName("ChallengeModeRank");
            writer.WriteStartObject();
            writer.WritePropertyName("RawCode");
            writer.WriteNumberValue(value.ChallengeModeRank.RawCode);
            writer.WriteEndObject();
            writer.WritePropertyName("Money");
            JsonSerializer.Serialize(writer, value.Money, PhiSaveJsonContext.Default.Money);
            writer.WritePropertyName("UnlockFlagOfSpasmodic");
            writer.WriteStringValue(value.UnlockFlagOfSpasmodic.ToString());
            writer.WritePropertyName("UnlockFlagOfIgallta");
            writer.WriteStringValue(value.UnlockFlagOfIgallta.ToString());
            writer.WritePropertyName("UnlockFlagOfRrharil");
            writer.WriteStringValue(value.UnlockFlagOfRrharil.ToString());
            writer.WritePropertyName("FlagOfSongRecordKey");
            writer.WriteStringValue(value.FlagOfSongRecordKey.ToString());
            writer.WritePropertyName("Node2");
            if (value.Node2 == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, value.Node2, PhiSaveJsonContext.Default.GameProgressNodeVersion2);
            }
            writer.WriteEndObject();
        }

        private static Challenge ParseChallenge(JsonElement root)
        {
            if (!TryGetProperty(root, "ChallengeModeRank", out var e) || e.ValueKind != JsonValueKind.Object)
            {
                return new Challenge((ushort)0);
            }

            return new Challenge((ushort)GetInt(e, "RawCode", 0));
        }

        private static Money ParseMoney(JsonElement root)
        {
            if (!TryGetProperty(root, "Money", out var e) || e.ValueKind != JsonValueKind.Object)
            {
                return new Money(0, 0, 0, 0, 0);
            }

            return new Money(
                (short)GetInt(e, "KiB", 0),
                (short)GetInt(e, "MiB", 0),
                (short)GetInt(e, "GiB", 0),
                (short)GetInt(e, "TiB", 0),
                (short)GetInt(e, "PiB", 0)
            );
        }

        private static GameProgressNodeVersion2? ParseNode2(JsonElement root)
        {
            if (!TryGetProperty(root, "Node2", out var n2) || n2.ValueKind != JsonValueKind.Object) return null;

            var node3 = ParseNode3(n2);
            return new GameProgressNodeVersion2(
                ParseEnum(n2, "RandomVersionUnlocked", (RandomVersionFlag)0),
                node3
            );
        }

        private static GameProgressNodeVersion3? ParseNode3(JsonElement node2)
        {
            if (!TryGetProperty(node2, "Node3", out var n3) || n3.ValueKind != JsonValueKind.Object) return null;

            var node4 = ParseNode4(n3);
            return new GameProgressNodeVersion3(
                ParseEnum(n3, "Chapter8UnlockFlag", (Chapter8UnlockFlag)0),
                ParseEnum(n3, "Chapter8SongUnlockFlag", (DifficultyUnlockFlag)0),
                node4
            );
        }

        private static GameProgressNodeVersion4? ParseNode4(JsonElement node3)
        {
            if (!TryGetProperty(node3, "Node4", out var n4) || n4.ValueKind != JsonValueKind.Object) return null;

            return new GameProgressNodeVersion4(
                ParseEnum(n4, "FlagOfSongRecordKeyTakumi", (TakumiUnlockFlag)0)
            );
        }
    }

    private sealed class GameKeyFlagJsonConverter : JsonConverter<GameKeyFlag>
    {
        public override GameKeyFlag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var typeByte = ParseGameKeyType(root, "Type", 0);
            var bytes = new List<byte>();

            if (TryGetProperty(root, "Flags", out var flagsNode) && flagsNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var flag in GetGameKeyFlagEntries(typeByte))
                {
                    if (!TryGetProperty(flagsNode, flag.Name, out var value))
                    {
                        bytes.Add(0);
                        continue;
                    }

                    bytes.Add(ReadFlagByte(flag.Name, value));
                }
            }
            else
            {
                var payload = GetULong(root, "Payload", 0UL);
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((typeByte & (1 << bit)) != 0)
                    {
                        bytes.Add((byte)((payload >> (bit * 8)) & 0xFF));
                    }
                }
            }

            return new GameKeyFlag(typeByte, bytes.ToArray());
        }

        public override void Write(Utf8JsonWriter writer, GameKeyFlag value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Type");
            writer.WriteStringValue(FormatGameKeyType((byte)value.Type));
            writer.WritePropertyName("Flags");
            writer.WriteStartObject();
            var payloadBytes = new List<byte>(GetGameKeyFlagPayloadBytes((byte)value.Type, value.Payload));
            var index = 0;
            foreach (var flag in GetGameKeyFlagEntries((byte)value.Type))
            {
                var flagValue = index < payloadBytes.Count ? payloadBytes[index] : (byte)0;
                index++;

                writer.WritePropertyName(flag.Name);
                if (flag.IsNumeric)
                {
                    writer.WriteNumberValue(flagValue);
                }
                else
                {
                    writer.WriteBooleanValue(flagValue != 0);
                }
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static IEnumerable<(string Name, bool IsNumeric)> GetGameKeyFlagEntries(byte typeByte)
        {
            if ((typeByte & (1 << 0)) != 0) yield return ("HasReadCollectionPieceCount", true);
            if ((typeByte & (1 << 1)) != 0) yield return ("HasUnlockedSingle", false);
            if ((typeByte & (1 << 2)) != 0) yield return ("HasUnlockedCollectionPieceCount", true);
            if ((typeByte & (1 << 3)) != 0) yield return ("HasUnlockedIllustration", false);
            if ((typeByte & (1 << 4)) != 0) yield return ("HasUnlockedAvatar", false);
        }

        private static IEnumerable<byte> GetGameKeyFlagPayloadBytes(byte typeByte, ulong payload)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                if ((typeByte & (1 << bit)) != 0)
                {
                    yield return (byte)((payload >> (bit * 8)) & 0xFF);
                }
            }
        }

        private static byte ReadFlagByte(string name, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.True) return 1;
            if (value.ValueKind == JsonValueKind.False) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetByte(out var b)) return b;
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (bool.TryParse(text, out var boolValue)) return (byte)(boolValue ? 1 : 0);
                if (byte.TryParse(text, out var parsedByte)) return parsedByte;
            }

            return name.Contains("Count", StringComparison.OrdinalIgnoreCase) ? (byte)0 : (byte)0;
        }
    }

    private sealed class GameUserInfoJsonConverter : JsonConverter<GameUserInfo>
    {
        public override GameUserInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            return new GameUserInfo(
                (byte)GetInt(root, "Version", 0),
                GetBool(root, "ShowUserId", false),
                GetString(root, "Intro", string.Empty),
                GetString(root, "AvatarId", string.Empty),
                GetString(root, "BackgroundId", string.Empty)
            );
        }

        public override void Write(Utf8JsonWriter writer, GameUserInfo value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Version");
            writer.WriteNumberValue(value.Version);
            writer.WritePropertyName("ShowUserId");
            writer.WriteBooleanValue(value.ShowUserId);
            writer.WritePropertyName("Intro");
            writer.WriteStringValue(value.Intro);
            writer.WritePropertyName("AvatarId");
            writer.WriteStringValue(value.AvatarId);
            writer.WritePropertyName("BackgroundId");
            writer.WriteStringValue(value.BackgroundId);
            writer.WriteEndObject();
        }
    }

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new Money(
                (short)GetInt(root, "KiB", 0),
                (short)GetInt(root, "MiB", 0),
                (short)GetInt(root, "GiB", 0),
                (short)GetInt(root, "TiB", 0),
                (short)GetInt(root, "PiB", 0)
            );
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("KiB");
            writer.WriteNumberValue(value.KiB);
            writer.WritePropertyName("MiB");
            writer.WriteNumberValue(value.MiB);
            writer.WritePropertyName("GiB");
            writer.WriteNumberValue(value.GiB);
            writer.WritePropertyName("TiB");
            writer.WriteNumberValue(value.TiB);
            writer.WritePropertyName("PiB");
            writer.WriteNumberValue(value.PiB);
            writer.WriteEndObject();
        }
    }

    private sealed class GameProgressNodeVersion2JsonConverter : JsonConverter<GameProgressNodeVersion2>
    {
        public override GameProgressNodeVersion2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var node3 = TryGetProperty(root, "Node3", out var n3) && n3.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize(n3.GetRawText(), PhiSaveJsonContext.Default.GameProgressNodeVersion3)
                : null;
            return new GameProgressNodeVersion2(ParseEnum(root, "RandomVersionUnlocked", (RandomVersionFlag)0), node3);
        }

        public override void Write(Utf8JsonWriter writer, GameProgressNodeVersion2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("RandomVersionUnlocked");
            writer.WriteStringValue(value.RandomVersionUnlocked.ToString());
            writer.WritePropertyName("Node3");
            if (value.Node3 == null) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value.Node3, PhiSaveJsonContext.Default.GameProgressNodeVersion3);
            writer.WriteEndObject();
        }
    }

    private sealed class GameProgressNodeVersion3JsonConverter : JsonConverter<GameProgressNodeVersion3>
    {
        public override GameProgressNodeVersion3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var node4 = TryGetProperty(root, "Node4", out var n4) && n4.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize(n4.GetRawText(), PhiSaveJsonContext.Default.GameProgressNodeVersion4)
                : null;
            return new GameProgressNodeVersion3(
                ParseEnum(root, "Chapter8UnlockFlag", (Chapter8UnlockFlag)0),
                ParseEnum(root, "Chapter8SongUnlockFlag", (DifficultyUnlockFlag)0),
                node4);
        }

        public override void Write(Utf8JsonWriter writer, GameProgressNodeVersion3 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Chapter8UnlockFlag");
            writer.WriteStringValue(value.Chapter8UnlockFlag.ToString());
            writer.WritePropertyName("Chapter8SongUnlockFlag");
            writer.WriteStringValue(value.Chapter8SongUnlockFlag.ToString());
            writer.WritePropertyName("Node4");
            if (value.Node4 == null) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value.Node4, PhiSaveJsonContext.Default.GameProgressNodeVersion4);
            writer.WriteEndObject();
        }
    }

    private sealed class GameProgressNodeVersion4JsonConverter : JsonConverter<GameProgressNodeVersion4>
    {
        public override GameProgressNodeVersion4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new GameProgressNodeVersion4(ParseEnum(root, "FlagOfSongRecordKeyTakumi", (TakumiUnlockFlag)0));
        }

        public override void Write(Utf8JsonWriter writer, GameProgressNodeVersion4 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("FlagOfSongRecordKeyTakumi");
            writer.WriteStringValue(value.FlagOfSongRecordKeyTakumi.ToString());
            writer.WriteEndObject();
        }
    }

    // 辅助方法
    private static TEnum ParseEnum<TEnum>(JsonElement root, string name, TEnum fallback) where TEnum : struct, Enum
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;

        if (e.ValueKind == JsonValueKind.String)
        {
            var raw = e.GetString();
            if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<TEnum>(raw, true, out var parsedByName))
            {
                return parsedByName;
            }

            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsedInt))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), parsedInt);
            }
        }

        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), n);
        }

        return fallback;
    }

    private static byte ParseGameKeyType(JsonElement root, string name, byte fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;

        if (e.ValueKind == JsonValueKind.Number && e.TryGetByte(out var n))
        {
            return n;
        }

        if (e.ValueKind == JsonValueKind.String)
        {
            var raw = e.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (byte.TryParse(raw, out var numeric)) return numeric;

            byte typeMask = 0;
            foreach (var token in raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                typeMask |= token switch
                {
                    "HasReadCollectionPieceCount" => (byte)(1 << 0),
                    "HasUnlockedSingle" => (byte)(1 << 1),
                    "HasUnlockedCollectionPieceCount" => (byte)(1 << 2),
                    "HasUnlockedIllustration" => (byte)(1 << 3),
                    "HasUnlockedAvatar" => (byte)(1 << 4),
                    _ => (byte)0
                };
            }

            if (typeMask != 0) return typeMask;
        }

        return fallback;
    }

    private static string FormatGameKeyType(byte typeByte)
    {
        var entries = new List<string>();
        if ((typeByte & (1 << 0)) != 0) entries.Add("HasReadCollectionPieceCount");
        if ((typeByte & (1 << 1)) != 0) entries.Add("HasUnlockedSingle");
        if ((typeByte & (1 << 2)) != 0) entries.Add("HasUnlockedCollectionPieceCount");
        if ((typeByte & (1 << 3)) != 0) entries.Add("HasUnlockedIllustration");
        if ((typeByte & (1 << 4)) != 0) entries.Add("HasUnlockedAvatar");

        if (entries.Count == 0)
        {
            return typeByte.ToString();
        }

        return string.Join("|", entries);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement root, string name, string fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? fallback;
        return fallback;
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.True) return true;
        if (e.ValueKind == JsonValueKind.False) return false;
        if (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var v)) return v;
        return fallback;
    }

    private static int GetInt(JsonElement root, string name, int fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return n;
        if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var v)) return v;
        return fallback;
    }

    private static float GetFloat(JsonElement root, string name, float fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out var n)) return n;
        if (e.ValueKind == JsonValueKind.String && float.TryParse(e.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return fallback;
    }

    private static ulong GetULong(JsonElement root, string name, ulong fallback)
    {
        if (!TryGetProperty(root, name, out var e)) return fallback;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetUInt64(out var n)) return n;
        if (e.ValueKind == JsonValueKind.String && ulong.TryParse(e.GetString(), out var v)) return v;
        return fallback;
    }
}
