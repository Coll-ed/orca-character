using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;

namespace OrcaCharacter;

/// <summary>
///     ★★ <b>「把一笔伤害附加加到剑上」的<b>唯一</b>入口</b>（龙剑 <see cref="OrcaBloodSword" /> /
///     魔剑 <see cref="OrcaBloodBlade" />）。
///
///     <para><b>为什么要有这个类</b>：2026-10-07 之后有<b>三处</b>要往剑上加附加 ——
///     找剑的逻辑（<c>PlayerCombatState.AllCards</c> + 两种剑的 <c>OfType</c> + 空值处理 + 日志）
///     若各写一份，任何一处改口径都会漂移。消费者：
///     <list type="bullet">
///       <item><see cref="OrcaCrimsonTemper" />（红莲淬，权威 卡牌说明1.txt L21）
///         —— 把【焚烧】让敌人**实际**损失的生命全额加上去（两把剑都加）；</item>
///       <item><see cref="OrcaVoidReturnPower" />（归墟，权威 卡牌说明2.txt L54）
///         —— 把被拦下的回复按比例转上去（**只给魔剑**）；</item>
///       <item><see cref="OrcaVoidReturnBlockPatch" />（归墟在 <c>CreatureCmd.Heal</c> 源头的拦截）
///         —— 同上，只给魔剑。</item>
///     </list></para>
///
///     <para>★ <b>"携带的那把剑"怎么找</b>：<c>PlayerCombatState.AllCards</c> —— 不论那张牌此刻在
///     抽牌堆 / 手牌 / 弃牌堆 / 消耗堆都会覆盖到（实据与用法见 <see cref="OrcaFrenzySlash" /> 的类摘要，
///     与《狂热斩击》找剑是同一套）。正常一局里同一时刻只有一把
///     （魔剑由龙剑经「古老牙齿」转换而来）⇒ 两把都在时按 <see cref="OrcaSwordPick.Both" /> 都加。</para>
/// </summary>
internal static class OrcaSwordBonus
{
    /// <summary>剑名（日志用）。★ 与本地化文件里的卡名保持同一套字面（本地化在 JSON 里，无法与本文件同源）。</summary>
    internal const string BloodSwordName = "嗜血龙剑";

    /// <summary>剑名（日志用）。见 <see cref="BloodSwordName" /> 的说明。</summary>
    internal const string BloodBladeName = "嗜血魔剑";

    /// <summary>给哪把（哪几把）剑加附加。</summary>
    internal enum OrcaSwordPick
    {
        /// <summary>两把都加（红莲淬："龙剑/魔剑"）。</summary>
        Both,

        /// <summary>只加龙剑。</summary>
        BloodSword,

        /// <summary>只加魔剑（归墟：权威逐字写的是「转入魔剑的附加伤害」）。</summary>
        BloodBlade
    }

    /// <summary>
    ///     给<b>当前携带的</b>目标剑加 <paramref name="amount" /> 点伤害附加。
    ///
    ///     <para>★ <b>边界一律显式</b>：<paramref name="amount" /> ≤ 0 ⇒ 什么都不做（返回空表，不是错误）；
    ///     拿不到玩家 / 战斗状态 / 卡组里根本没有那把剑 ⇒ 返回空表，<b>由调用方</b>记日志
    ///     （本方法只知道"加没加上"，不知道调用方的语义要不要报警 —— 例如"归墟转化时没魔剑"是设计内的，
    ///     而"红莲淬时两把都没有"才值得报）。异常不静默：记 Warn 后返回已加上的部分。</para>
    /// </summary>
    /// <param name="me">玩家（可能为 null：不在战斗 / 尚未挂载）。</param>
    /// <param name="amount">要加的附加点数（&gt; 0 才生效）。</param>
    /// <param name="pick">目标剑（见 <see cref="OrcaSwordPick" />）。</param>
    /// <returns>每把**真的被加到**的剑：(名字, 加完之后的累计附加)。</returns>
    internal static List<(string Name, int After)> Add(
        Player? me, int amount, OrcaSwordPick pick)
    {
        var hits = new List<(string Name, int After)>();
        if (amount <= 0) return hits;

        try
        {
            var all = me?.PlayerCombatState?.AllCards;
            if (all == null)
            {
                OrcaLog.Warn($"[Orca] 剑·伤害附加 +{amount} 未生效：拿不到 PlayerCombatState.AllCards");
                return hits;
            }

            if (pick != OrcaSwordPick.BloodBlade)
            {
                foreach (var sword in all.OfType<OrcaBloodSword>())
                {
                    sword.AddBonus(amount);
                    hits.Add((BloodSwordName, sword.Bonus));
                }
            }

            if (pick != OrcaSwordPick.BloodSword)
            {
                foreach (var blade in all.OfType<OrcaBloodBlade>())
                {
                    blade.AddBonus(amount);
                    hits.Add((BloodBladeName, blade.Bonus));
                }
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 剑·伤害附加 +{amount} 出错（已加上的部分照旧生效）：{ex.Message}");
        }

        return hits;
    }

    /// <summary>把命中结果拼成日志尾巴；没有命中时给出"卡组里没有那把剑"的说明。</summary>
    internal static string Describe(List<(string Name, int After)> hits, OrcaSwordPick pick)
    {
        if (hits.Count == 0)
        {
            string want = pick switch
            {
                OrcaSwordPick.BloodSword => BloodSwordName,
                OrcaSwordPick.BloodBlade => BloodBladeName,
                _ => $"{BloodSwordName} / {BloodBladeName}"
            };
            return $"卡组里没有【{want}】⇒ 这笔附加上不去";
        }

        return string.Join("、", hits.Select(h => $"【{h.Name}】附加 {h.After}"));
    }
}
