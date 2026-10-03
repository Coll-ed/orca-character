using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace OrcaCharacter;

/// <summary>
///     ★ 补齐（2026-10-03）：按官方 dll 的反编译版补回本补丁 ✓
///     —— 与官方版本逐文件 diff 的唯一一处"我方缺失" ✗。
///
///     <para>作用：有人**直接调用 <c>CreatureCmd.SetCurrentHp</c>** 把生命设到 0 或负数时，
///     若该生物身上挂着 <see cref="OrcaBloodNirvanaPower" />（浴血涅槃），
///     就把掉血量**压到只剩 1 点生命**，并打上 pending 标记交异步钩子结算 ✓
///     （掉血管线的拦截在 <c>ModifyHpLostAfterOsty</c> 里 ✓ 两条路径互补）。</para>
///
///     <para>边界显式（纪律 #4）：不满足条件时**原样放行**，绝不静默改动别人的数值 ✓。</para>
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), "SetCurrentHp")]
internal static class OrcaNirvanaSelfHarmPatch
{
    private static void Prefix(Creature creature, ref decimal amount)
    {
        try
        {
            if (creature == null) return;
            var power = creature.GetPower<OrcaBloodNirvanaPower>();
            if (power == null) return;
            if (amount > 0m) return;              // 只在"会致死/致负"时才拦 ✓

            amount = 1m;                          // 压到剩 1 点生命
            power.MarkPending();                  // 交给异步钩子结算
        }
        catch (Exception ex)
        {
            // 边界显式：绝不静默吞掉 ✓
            OrcaLog.Warn($"[Orca] 浴血涅槃·SetCurrentHp 拦截出错（本次放行）：{ex.Message}", 2);
        }
    }
}
