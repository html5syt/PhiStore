using System;
using System.Collections.Generic;
using System.Linq;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Models;
using PhiStore.Addons.PhiSave2.Internal;

namespace PhiStore.Addons.PhiSave2;

// PhiSave2Service：定义、RKS计算

/// <summary>
/// 纯 C# 实现的 PhiSave2 业务逻辑类。
/// 不依赖 Godot 引擎，专注于业务逻辑和性能。
/// </summary>
public partial class PhiSave2Service : IDisposable
{
    // ======定义======
    public const string DefaultClientId = "rAK3FfdieFob2Nn8Am";
    public const string DefaultClientKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

    private string _sessionToken = string.Empty;
    private string _userObjectId = string.Empty;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string? _customCloudServer = null;
    private Save? _saveObj = null;
    // 临时预览合并存储
    private PhiSaveData? _previewMerge = null;

    // 当不使用 Godot 的服务时，UI/主机可以订阅的事件
    public event Action<CompleteQRCodeData>? QrCodeAvailable;
    public event Action<TapTapTokenData?>? QrCodeCheckResult;
    public event Action<string>? OAuthUrlGenerated;
    /// <summary>
    /// (success, sessionTokenOrError)
    /// </summary>
    public event Action<bool, string>? LoginCompleted;

    public PhiSaveData? CurrentSave { get; set; }

    public string SessionToken => _sessionToken;
    public string UserObjectId => _userObjectId;

    public bool IsLoggedIn => _saveObj != null;

    public record SaveMetadata(DateTime? LocalModifiedUtc, DateTime? CloudModifiedUtc, float LocalRks, float CloudRks);

    public record ScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc);

    public record DetailedScoreDiff(string SongId, int DifficultyIndex, int? LocalScore, float? LocalAcc, int? CloudScore, float? CloudAcc, string Suggestion);

    public record DetailedScoreDiffWithObjects(DetailedScoreDiff Diff, SongScore? LocalObj, SongScore? CloudObj);

    public record SyncResult(string Status, string Message, List<ScoreDiff>? Diffs = null, string? FileId = null, string? ObjId = null);

    public record FullDiff(string Path, string DiffType, string? LocalValue, string? CloudValue);

    // ===== RKS 计算 =====
    public record RksEntry(string SongId, int DifficultyIndex, string DifficultyName, float DifficultyValue, float Acc, float Rks, bool IsPhi);
    public record RksDetails(List<RksEntry> Entries, float SumB27, float SumTop3Phi, float TotalRks);

    private static bool TryResolveDifficultyLocal(Dictionary<string, float> difficulties, string songId, int diffIdx, string diffName, out float diffNum)
    {
        diffNum = 0f;
        if (difficulties == null) return false;
        var key = $"{songId}_{diffIdx}";
        if (difficulties.TryGetValue(key, out diffNum)) return true;

        if (!string.IsNullOrWhiteSpace(diffName))
        {
            key = $"{songId}_{diffName}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
            var upper = diffName.ToUpperInvariant();
            key = $"{songId}_{upper}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
        }

        string? alias = diffIdx switch
        {
            0 => "EZ",
            1 => "HD",
            2 => "IN",
            3 => "AT",
            4 => "LEGACY",
            _ => null
        };

        if (!string.IsNullOrEmpty(alias))
        {
            key = $"{songId}_{alias}";
            if (difficulties.TryGetValue(key, out diffNum)) return true;
        }

        return false;
    }

    /// <summary>
    /// 为当前 `CurrentSave` 中已存在的成绩项计算每首歌的 RKS 条目以及总体汇总值。
    /// difficulties 参数为歌曲难度映射（键格式："{songId}_{difficultyNameOrIndex}" -> 数值难度）。
    /// 返回包含逐项条目与聚合指标的 <see cref="RksDetails"/>。
    /// </summary>
    /// <param name="difficulties">映射歌曲难度的字典，用于计算单首曲目的 RKS。</param>
    /// <returns>包含条目列表及汇总 RKS 值的 <see cref="RksDetails"/> 对象。</returns>
    public RksDetails CalculateRksDetails(Dictionary<string, float> difficulties)
    {
        var entries = new List<RksEntry>();
        if (CurrentSave?.Record?.Records == null) return new RksDetails(entries, 0f, 0f, 0f);

        var items = new List<DifficultyItem>();

        foreach (var s in CurrentSave.Record.Records)
        {
            var songId = s.Id;
            var diffEnum = s.Difficulty;
            int diffIdx = (int)diffEnum;
            var diffName = diffEnum.ToString();
            if (TryResolveDifficultyLocal(difficulties, songId, diffIdx, diffName, out var diffVal) && diffVal > 0f)
            {
                items.Add(new DifficultyItem { SongId = songId, Difficulty = diffVal, Acc = s.Accuracy });
                var rks = RKSCalculator.CalculateSingleRks(s.Accuracy, diffVal);
                entries.Add(new RksEntry(songId, diffIdx, diffName, diffVal, s.Accuracy, rks, s.Accuracy >= 100f));
            }
        }

        // 使用相同的算法计算汇总指标
        var allCalculated = items
            .Select(i => new { Item = i, Rks = RKSCalculator.CalculateSingleRks(i.Acc, i.Difficulty), IsPhi = i.Acc >= 100f })
            .Where(x => x.Rks > 0f)
            .OrderByDescending(x => x.Rks)
            .ToList();

        var sumB27 = allCalculated.Take(27).Sum(x => x.Rks);
        var sumTop3Phi = allCalculated.Where(x => x.IsPhi).Take(3).Sum(x => x.Rks);
        var total = (float)Math.Round((sumB27 + sumTop3Phi) / 30f, 4);

        return new RksDetails(entries, (float)Math.Round(sumB27, 4), (float)Math.Round(sumTop3Phi, 4), total);
    }

    /// <summary>
    /// 根据传入的 difficulty map 计算 RKS 并应用到 CurrentSave（同时更新 GameSummary.Rks 如果存在）。
    /// 返回计算得到的 total RKS 值。
    /// </summary>
    public float ComputeAndApplyInMemoryRks(Dictionary<string, float> difficulties)
    {
        var details = CalculateRksDetails(difficulties);
        if (CurrentSave == null) CurrentSave = new PhiSaveData();
        CurrentSave.SummaryRks = details.TotalRks;
        if (CurrentSave.GameSummary != null)
        {
            CurrentSave.GameSummary.Rks = details.TotalRks;
        }
        return details.TotalRks;
    }

}
