using System;
using System.Collections.Generic;
using System.Linq;

namespace PhiStore.Addons.PhiSave2.Internal;

/// <summary>
/// 表示单首歌的一个定数数据项
/// 为了解耦，调用方自己传入谱面定数。
/// </summary>
public class DifficultyItem
{
    public string SongId { get; set; } = string.Empty;
    public float Difficulty { get; set; }
    public float Acc { get; set; }
}

/// <summary>
/// RKS 计算器
/// </summary>
public static class RKSCalculator
{
    /// <summary>
    /// 计算单曲的 RKS 值
    /// rks = ((acc - 55) / 45) ^ 2 * Difficulty
    /// Only when acc > 70
    /// </summary>
    public static float CalculateSingleRks(float acc, float difficulty)
    {
        if (acc <= 70f) return 0f;
        var r = (acc - 55f) / 45f;
        return (float)Math.Round(r * r * difficulty, 4);
    }

    /// <summary>
    /// 整合策略：B27 + Top3Phi
    /// </summary>
    public static float CalculateTotalRks(IEnumerable<DifficultyItem> items)
    {
        var allCalculated = items
            .Select(i => new
            {
                Item = i,
                Rks = CalculateSingleRks(i.Acc, i.Difficulty),
                IsPhi = i.Acc >= 100f // Phigros 中满 ACC 即为 Phi
            })
            .Where(x => x.Rks > 0f)
            .OrderByDescending(x => x.Rks)
            .ToList();

        // 选取前 27 名的成绩
        var b27 = allCalculated.Take(27);
        // 筛选出已 Phi 的成绩，取其前 3 名
        var top3Phi = allCalculated.Where(x => x.IsPhi).Take(3);

        var sumB27 = b27.Sum(x => x.Rks);
        var sumTop3Phi = top3Phi.Sum(x => x.Rks);

        return (float)Math.Round((sumB27 + sumTop3Phi) / 30f, 4);
    }
}
