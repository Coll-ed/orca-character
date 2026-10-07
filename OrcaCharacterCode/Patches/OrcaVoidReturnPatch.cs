using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **归墟的真实拦截**（用户 2026-09-23：「归墟没跑阿，字面意思拦住回血并转入，
///     但其实回了血」）。
///
///     <para><b>为什么原来的写法没用</b>：<see cref="OrcaVoidReturnPower" /> 走的是
///     <c>AfterCurrentHpChanged</c> 的**事后撤销** ——
///     <c>await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - delta)</c>。
///     实机结果是**日志照打、血照样回**：那次撤销被引擎自己的治疗流程盖掉了
///     （恢复的血量在引擎侧还有后续写入）。"事后扣回来"这条路不可靠。</para>
///
///     <para><b>改成在源头拦</b>：给 <c>CreatureCmd.Heal(Creature, Decimal, Boolean)</c> 挂 Prefix，
///     直接把 <c>ref amount</c> 归零 ⇒ **血根本不会回上去**，不需要事后补偿。
///     这条挂点已经被 <see cref="OrcaHealBonusPatch" />（焚文的治疗加成）**实机验证有效**
///     （日志里 <c>治疗加成 50%：4 → 6</c> 真实生效）⇒ 同一条路，可靠。</para>
///
///     <para><b>"场上所有角色"怎么判</b>：归墟挂在**本地玩家**身上，而治疗入口是全局的
///     ⇒ 只要本地玩家身上有归墟，**任何生物**（玩家 / 敌人 / 队友）的治疗都被拦下。
///     这正是卡面写的"场上所有角色无法回复生命"。</para>
///
///     <para>★★ <b>2026-10-07 权威改版两处</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L52-54）：
///     <list type="number">
///       <item><b>转入对象是【嗜血魔剑】</b>（权威逐字「转入<b>魔剑</b>的附加伤害」）—— 改前加的是
///         【嗜血龙剑】，与权威不符；加附加的唯一入口 = <see cref="OrcaSwordBonus.Add" />；</item>
///       <item><b>不拦截吸血</b>：吸血那一路必须照常回血 ⇒ 进 <see cref="OrcaLifestealHeal" /> 闸门
///         （由吸血结算 / 魔剑单敌自愈两处标注），本 Prefix 直接放行。</item>
///     </list></para>
///
///     <para>⚠️ 反向兜底仍然保留 <see cref="OrcaVoidReturnPower.AfterCurrentHpChanged" />：
///     万一有治疗绕过了 <c>CreatureCmd.Heal</c>，那条老路径还能补上（它同样会放行吸血闸门）。</para>
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), "Heal")]
internal static class OrcaVoidReturnBlockPatch
{
    private static void Prefix(Creature creature, ref decimal amount)
    {
        try
        {
            if (creature == null || amount <= 0) return;

            var power = ActiveOnLocalPlayer();
            if (power == null) return;

            // ★★ 权威 2026-10-07（卡牌说明2.txt L54）：「**不拦截吸血**」
            //    ⇒ 吸血那一路的回血照常放行。闸门由 OrcaLifestealHeal 标注，
            //      两个生产端（吸血结算 / 魔剑单敌自愈）见其类摘要。
            if (OrcaLifestealHeal.InFlight)
            {
                OrcaLog.Info($"[Orca] 归墟：{creature.Name} 的 {amount} 点回复来自【吸血】"
                           + " ⇒ 按权威**放行**（不拦截吸血）", 2);
                return;
            }

            decimal blocked = amount;
            amount = 0m;                                   // ★ 真正拦下：血不会回上去

            int converted = (int)Math.Floor(blocked * power.Ratio);
            string tail;
            if (converted > 0)
            {
                // ★ 2026-10-07 权威改版：转入对象是【嗜血魔剑】（权威逐字写「转入**魔剑**的附加伤害」），
                //   不再是【嗜血龙剑】。加附加的唯一入口 = OrcaSwordBonus.Add（三处消费者共用）。
                var pick = OrcaSwordBonus.OrcaSwordPick.BloodBlade;
                var hits = OrcaSwordBonus.Add(LocalPlayer(), converted, pick);
                tail = $"{converted} 点（{OrcaVoidReturnPower.PercentFromRatio(power.Ratio)}%）"
                     + $"转入{OrcaSwordBonus.Describe(hits, pick)}";
            }
            else
            {
                tail = "不足 1 点可转化";
            }

            OrcaLog.Info($"[Orca] 归墟：{creature.Name} 的 {blocked} 点回复被**拦下** ⇒ {tail}", 2);
        }
        catch (Exception ex)
        {
            // ★ 出错必须放行（amount 保持原值）—— 绝不能因为归墟把治疗系统弄崩
            OrcaLog.Warn($"[Orca] 归墟拦截出错（本次放行治疗）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     本地玩家身上有没有归墟（有就返回那个 Power，没有返回 null）。
    ///     <para>取值路径与 <c>OrcaAudio.LocalPlayerIsOrca</c> 同一套（都已实机验证）：
    ///     <c>RunManager.Instance.DebugOnlyGetState()</c>（纯取值）→ <c>RunState.Players</c>
    ///     → <c>LocalContext.GetMe(IEnumerable&lt;Player&gt;)</c>。</para>
    /// </summary>
    internal static OrcaVoidReturnPower? ActiveOnLocalPlayer()
    {
        try
        {
            var players = RunManager.Instance?.DebugOnlyGetState()?.Players;
            if (players == null) return null;
            Player? me = LocalContext.GetMe(players);
            return me?.Creature?.GetPower<OrcaVoidReturnPower>();
        }
        catch (Exception ex)
        {
            // ★ 2026-10-07：原来是裸 catch（静默吞）—— 与工程纪律「禁止静默吞异常」不符，补记录。
            //   注意：这里**不改变行为**（仍然返回 null = 本次不拦治疗），只是让根因可查。
            OrcaLog.Warn($"[Orca] 归墟：查本地玩家身上的归墟失败（本次不拦治疗）：{ex.Message}", 2);
            return null;
        }
    }

    /// <summary>
    ///     本地玩家（拿不到返回 null）。
    ///     <para>取值路径与 <see cref="ActiveOnLocalPlayer" /> **同一套**（都已实机验证）：
    ///     <c>RunManager.Instance.DebugOnlyGetState()</c>（纯取值）→ <c>RunState.Players</c>
    ///     → <c>LocalContext.GetMe(IEnumerable&lt;Player&gt;)</c>。
    ///     ★ 边界显式：异常记 Warn 后返回 null（不静默吞）。</para>
    /// </summary>
    internal static Player? LocalPlayer()
    {
        try
        {
            var players = RunManager.Instance?.DebugOnlyGetState()?.Players;
            return players == null ? null : LocalContext.GetMe(players);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 归墟：取本地玩家失败（本次不加剑附加）：{ex.Message}", 2);
            return null;
        }
    }
}