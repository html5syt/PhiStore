using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhigrosLibraryCSharp.CloudSave;

namespace PhiStore.Addons.PhiSave2.Models;

/// <summary>
/// 完整云存档数据抽象（支持JSON序列化/反序列化及与 PhigrosLibraryCSharp 中的模型互转）
/// </summary>
public class PhiSaveData
{
    [JsonPropertyName("gameRecord")]
    public GameRecord? Record { get; set; }

    [JsonPropertyName("gameProgress")]
    public GameProgress? Progress { get; set; }

    [JsonPropertyName("gameUserInfo")]
    public GameUserInfo? UserInfo { get; set; }

    [JsonPropertyName("gameSettings")]
    public GameSettings? Settings { get; set; }

    [JsonPropertyName("gameKeys")]
    public Dictionary<string, GameKeyFlag> Keys { get; set; } = new();

    /// <summary>
    /// 完整解析出的 Summary 数据片段
    /// </summary>
    [JsonIgnore]
    public Summary? GameSummary { get; set; }

    /// <summary>
    /// 缓存的 RKS 值，仅用于上传 Summary
    /// </summary>
    [JsonIgnore]
    public float SummaryRks { get; set; }

    /// <summary>
    /// 内存存档的修改（创建）时间。在需要与云端对比时作为本地存档的修改时间。
    /// </summary>
    [JsonIgnore]
    public System.DateTime ModifiedAt { get; set; } = System.DateTime.UtcNow;

    /// <summary>
    /// 从 SaveContext 提取全部数据
    /// </summary>
    public static PhiSaveData FromSaveContext(SaveContext ctx)
    {
        var data = new PhiSaveData();
        data.Record = ctx.ReadGameRecord();
        data.Progress = ctx.ReadGameProgress();
        data.UserInfo = ctx.ReadGameUserInfo();
        data.Settings = ctx.ReadGameSettings();

        var gameKey = ctx.ReadGameKey();
        if (gameKey != null)
        {
            data.Keys = new Dictionary<string, GameKeyFlag>(gameKey.Keys);
        }

        var summary = ctx.ReadSummary();
        if (summary != null)
        {
            data.GameSummary = summary;
            data.SummaryRks = summary.Rks;
        }

        return data;
    }

    /// <summary>
    /// 回写数据到 SaveContext
    /// </summary>
    public void WriteToContext(SaveContext ctx)
    {
        if (this.Record != null)
            ctx.SaveGameRecord(this.Record);

        if (this.Progress != null)
            ctx.SaveGameProgress(this.Progress);

        if (this.UserInfo != null)
            ctx.SaveGameUserInfo(this.UserInfo);

        if (this.Settings != null)
            ctx.SaveGameSettings(this.Settings);

        if (this.Keys.Count > 0)
        {
            var gameKey = new GameKey(0, this.Keys, 0, null);
            ctx.SaveGameKey(gameKey);
        }
    }
}