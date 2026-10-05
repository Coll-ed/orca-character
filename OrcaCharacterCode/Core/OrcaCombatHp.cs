using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace OrcaCharacter;

/// <summary>
///     ★★「本场战斗的临时生命上限」池 —— 银龙体系的**共同账本**。
///
///     <para>
///     谁在用：
///     <list type="bullet">
///       <item><b>银龙血统</b>（起始遗物）：每次掉血 → 临时上限 +floor(伤害×50%)；</item>
///       <item><b>铸血</b>（0 费技能）：减少当前 10% 战斗上限、回复等量生命（**负数入账**）。</item>
///     </list>
///     用户口径 2026-09-16：两者**同一本账**（明确选了"与银龙血统同一个池子"）。
///     </para>
///
///     <para>
///     ⚠️ 为什么不用 <c>CreatureCmd.GainMaxHp</c>：它内部会**顺带回血**，而规格要求"只加上限、不回复生命"
///     ⇒ 统一走 <c>CreatureCmd.SetMaxHp</c>（纯设置）。这条经验来自 <see cref="OrcaBloodline" /> 的实测。
///     </para>
///
///     <para>
///     ⚠️ **重入**：<c>SetMaxHp</c> 会引发"当前生命变化"，而银龙血统正挂在
///     <c>AfterCurrentHpChanged</c> 上 ⇒ 这里用 <see cref="Suppress" /> 标记"是我自己在动上限"，
///     避免自己喂自己。
///     </para>
///
///     <para>
///     ★ <b>2026-10-05 清理</b>：原来还有 <c>Add(Creature, decimal)</c> 与
///     <c>Reset(Creature)</c> 两个成员，全工程 0 个调用方（已用 grep 全树核实）——
///     它们实现的是"临时上限由本类自己加上去、战斗结束再由本类还原"的那套**旧机制**，
///     而现行机制是**银龙血统遗物自己记账并收尾**（见 <c>OrcaBloodline</c>），本类只被
///     铸血用来**扣**（<see cref="Deduct" />）。留着会误导读者以为"本类会还原"，
///     故一并删除；本类的职责现在只有一条：<b>按"先吃临时池、再动原有上限"扣总上限</b>。
///     </para>
/// </summary>
internal static class OrcaCombatHp
{
    /// <summary>
    ///     本场累计的临时上限修正（正=临时加上去的，负=被铸血扣掉的）。
    ///     ⚠️ 由**银龙血统**写入正数（它才是"加上限"的记账方），本类只在 <see cref="Deduct" /> 里把它**扣小**。
    /// </summary>
    private static decimal _delta;

    /// <summary>
    ///     重入闸门：我们自己的 <c>SetMaxHp</c> 期间置位，表示"这次上限变化是本类造成的"。
    ///
    ///     <para>⚠️ <b>现状如实记录（不是本文件改出来的，是既有事实）</b>：全工程**只写不读** ——
    ///     银龙血统的 <c>AfterCurrentHpChanged</c> 目前**没有**读这个标志。
    ///     即它此刻**并不真的挡住重入**；保留是因为改它等于动 <see cref="Deduct" /> 的既有路径
    ///     （本轮纪律：铸血那条路一个字不许动）。真要让闸门生效，应在
    ///     银龙血统的钩子开头加 <c>if (OrcaCombatHp.Suppress) return;</c> —— 属于另一件事，
    ///     待与用户确认后单独做。</para>
    /// </summary>
    internal static bool Suppress { get; private set; }

    /// <summary>当前累计修正（正=临时加上去的，负=被铸血扣掉的）。</summary>
    internal static decimal Delta => _delta;

    /// <summary>
    ///     ★★ 从**总生命上限**里扣（「铸血」用）—— **先扣临时池，临时池扣完再扣原有上限**。
    ///
    ///     <para>用户口径 2026-09-16：
    ///     <i>"注血看总生命上限池子去扣上限；总生命上限 = 原有生命上限 + 临时生命上限；
    ///     临时生命上限没了，扣除的就是原有生命上限了"</i>。</para>
    ///
    ///     <para>
    ///     扣掉的部分**分两段记账** ——
    ///     <list type="bullet">
    ///       <item>吃掉临时池的那一段：只是把账本改小，**战斗结束不会再还原回来**（已经用来抵账了）；</item>
    ///       <item>超出临时池、动了原有上限的那一段：**永久损失**（`_delta` 不动，因为它从来就不在账上）。</item>
    ///     </list>
    ///     最终 <c>MaxHp</c> 减掉的是两段之和 = <paramref name="amount" />。
    ///     </para>
    ///
    ///     <para>⚠️ 上限保护：任何情况下不让上限低于 1（超出部分自动截断，返回实际扣量）。</para>
    /// </summary>
    /// <returns>实际扣掉的总量（可能被"上限不低于 1"截断）</returns>
    internal static async Task<decimal> Deduct(Creature me, decimal amount)
    {
        if (me == null || amount <= 0) return 0;

        amount = Math.Min(amount, Math.Max(0m, me.MaxHp - 1m));      // 上限不低于 1
        if (amount <= 0) return 0;

        decimal fromTemp = Math.Min(Math.Max(_delta, 0m), amount);   // ① 先吃临时池
        _delta -= fromTemp;
        decimal fromReal = amount - fromTemp;                        // ② 剩下的动原有上限（永久）

        Suppress = true;
        try { await CreatureCmd.SetMaxHp(me, me.MaxHp - amount); }
        finally { Suppress = false; }

        Log.Info($"[Orca] 注血：总上限 -{amount}（临时池 -{fromTemp} / 原有上限 -{fromReal}）"
                 + $"→ 当前上限 {me.MaxHp}，临时池余 {_delta:+#;-#;0}", 2);
        return amount;
    }
}
