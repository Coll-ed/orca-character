using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace OrcaCharacter;

/// <summary>
///     ★★ <b>死亡情况记录器（挂点 A：死亡那一批）</b> —— 补 <c>CreatureCmd.Kill</c> 的**前置**，
///     把"这一批死亡里有没有玩家、还带走了哪些敌人"交给 <see cref="OrcaDeathQuotes" />。
///
///     <para><b>为什么挂 <c>CreatureCmd.Kill</c> 而不是 <c>Hook.AfterDeath</c></b>
///     （任务书给的两个候选，这里选前者的理由）：
///     <list type="number">
///       <item><b>批次语义</b>：反编译实据（<c>CreatureCmd.Damage</c> L394-431）—— 一次伤害结算把所有
///         本批死者收进 <c>killedCreatures</c>，**最后合成一次</b> <c>await Kill(killedCreatures)</c>。
///         所以"同一次焚烧波及的死者在同一批"是引擎保证的；而 <c>Hook.AfterDeath</c> 是
///         <b>逐个</b>生物回调的，拿不到"这一批还有谁"。</item>
///       <item><b>重载歧义</b>：<c>Kill</c> 有两个重载（单个 / 集合），按本项目既有做法走
///         <c>TargetMethods()</c> 显式解析，避免 Harmony 因歧义**静默放弃**补丁
///         （同 <c>OrcaPoolRegistry.OrcaAllCardsPatch</c> 的注释所述）。</item>
///     </list></para>
///
///     <para>★★ <b>为什么必须是「前置」而不是「后置」</b>（第一版实现的实际缺陷，已修）：
///     <c>Kill</c> 体内 L488 就 <c>NRun.Instance.ShowGameOverScreen(...)</c> 建结束画面，
///     结束画面的 <c>_Ready</c>（L470）**同步**调 <c>InitializeBannerAndQuote()</c>，
///     在 L526 就把 <c>_deathQuote.Text</c> 写掉了 ⇒ 后置登记时**替换时机早已错过**，
///     玩家在结束画面上看到的仍是引擎原文（功能静默不生效）✗。
///     改成前置后：登记发生在"生物还没死、结束画面还没建"的时刻 ⇒ 紧接着的
///     <c>ShowGameOverScreen</c> → <c>_Ready</c> → 写文案时，记录已经在位 ✓。</para>
///
///     <para>⚠️ 这是**只读登记**：绝不改动 <paramref name="creatures" />、不拦流程、不改数值 ✓。</para>
/// </summary>
[HarmonyPatch]
internal static class OrcaDeathBatchPatch
{
    /// <summary>要补的 <c>CreatureCmd.Kill</c> 重载参数（集合版；单个版内部就转调它）。</summary>
    private static readonly Type[] KillSignature =
    {
        typeof(IReadOnlyCollection<Creature>),
        typeof(bool),
    };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var target = AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Kill), KillSignature);
        if (target == null)
        {
            // 边界显式：找不到目标就说清楚后果（死亡台词永久不生效），不静默。
            OrcaLog.Error("[Orca] 死亡台词：找不到 CreatureCmd.Kill(IReadOnlyCollection<Creature>, bool) ⇒ 本功能不生效", 2);
            return Array.Empty<MethodBase>();
        }

        OrcaLog.Info("[Orca] 死亡台词：已挂上 CreatureCmd.Kill 的死亡批次记录（前置）", 2);
        return new MethodBase[] { target };
    }

    /// <summary>
    ///     前置：在引擎真正处死这一批、并据此创建结束画面**之前**把批次记下来。
    ///     ⚠️ 此时生物尚未死亡，故 <see cref="OrcaDeathQuotes.NoteKillBatch" /> 只读
    ///     <c>IsPlayer</c> / <c>Side</c>（与生死无关）。
    /// </summary>
    private static void Prefix(IReadOnlyCollection<Creature> creatures)
    {
        try
        {
            OrcaDeathQuotes.NoteKillBatch(creatures);
        }
        catch (Exception ex)
        {
            // 边界显式：绝不能因为记台词把战斗流程打断（纪律 #4：记日志而非静默吞）。
            OrcaLog.Warn($"[Orca] 死亡台词：死亡批次记录出错（放行，不影响战斗）：{ex.Message}", 2);
        }
    }
}
