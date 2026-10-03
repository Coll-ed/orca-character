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
///     <c>AfterCurrentHpChanged</c> 上 ⇒ 必须屏蔽（见 <see cref="Suppress" />），否则会自己喂自己。
///     </para>
/// </summary>
internal static class OrcaCombatHp
{
    /// <summary>本场累计的临时上限修正（可为负）。</summary>
    private static decimal _delta;

    /// <summary>重入闸门：我们自己的 <c>SetMaxHp</c> 期间屏蔽钩子。</summary>
    internal static bool Suppress { get; private set; }

    /// <summary>当前累计修正（正=临时加上去的，负=被铸血扣掉的）。</summary>
    internal static decimal Delta => _delta;

    /// <summary>
    ///     按 <paramref name="amount" /> 调整本场战斗的临时上限（正数加、负数减）。
    ///     返回**实际**生效的调整量（可能被"上限不低于 1"截断）。
    /// </summary>
    internal static async Task<decimal> Add(Creature me, decimal amount)
    {
        if (me == null || amount == 0) return 0;

        const decimal floor = 1m;
        decimal target = me.MaxHp + amount;
        if (target < floor)
        {
            amount = floor - me.MaxHp;        // 截断到"上限不低于 1"
            target = floor;
        }
        if (amount == 0) return 0;

        _delta += amount;
        Suppress = true;
        try { await CreatureCmd.SetMaxHp(me, target); }
        finally { Suppress = false; }
        return amount;
    }

    /// <summary>战斗结束：把本场所有临时修正一次性还原，并清零账本。</summary>
    internal static async Task Reset(Creature me)
    {
        if (_delta == 0) return;
        decimal back = _delta;
        _delta = 0;
        if (me == null) return;

        Suppress = true;
        try { await CreatureCmd.SetMaxHp(me, Math.Max(1m, me.MaxHp - back)); }
        finally { Suppress = false; }
        Log.Info($"[Orca] 战斗临时血上限：还原 {-back:+#;-#;0} → 当前上限 {me.MaxHp}", 2);
    }

    /// <summary>
    ///     ★★ 从**总生命上限**里扣（「注血」用）—— **先扣临时池，临时池扣完再扣原有上限**。
    ///
    ///     <para>用户口径 2026-09-16：
    ///     <i>"注血看总生命上限池子去扣上限；总生命上限 = 原有生命上限 + 临时生命上限；
    ///     临时生命上限没了，扣除的就是原有生命上限了"</i>。</para>
    ///
    ///     <para>
    ///     与 <see cref="Add" />（负数入账 ⇒ 战斗结束会还原）的区别：
    ///     这里扣掉的部分**分两段记账** ——
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
