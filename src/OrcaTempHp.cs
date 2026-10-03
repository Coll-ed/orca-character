using System;

namespace OrcaCharacter;

/// <summary>
///     ★★ **战斗临时生命上限池** —— 本场战斗内累加、战斗结束由遗物还原。
///
///     <para><b>为什么要有它</b>：卡牌扩充第二版里有两张牌要动这个数值 ——
///     <list type="bullet">
///       <item>【浴血涅槃】：<i>"本回合当你受到致命伤害时，**消耗所有临时生命上限**，
///         回复 40%（消耗上限，向下取整）的生命"</i>；</item>
///       <item>【栖途】：<i>"战斗结束后，将你 25% 的临时生命上限**转化为真实生命上限**"</i>。</item>
///     </list>
///     而它原本只是 <see cref="OrcaBloodline" />（银龙血统遗物）的**私有字段** ⇒
///     抽成公共池，让「遗物 / 浴血涅槃 / 栖途」三处共享**同一份计数**，否则各自记账必然对不上。</para>
///
///     <para>⚠️ 本类**只做记账**：真正的上限修改仍由调用方执行
///     （<c>CreatureCmd.SetMaxHp</c>）—— 因为遗物那边有重入闸门 <c>_applying</c> 要配合
///     （自己改上限会再次触发「当前生命变化」钩子），不能让池子越过它去改数值。</para>
/// </summary>
internal static class OrcaTempHp
{
    private static decimal _temp;

    /// <summary>本场战斗累计的临时生命上限（战斗外为 0）。</summary>
    internal static decimal Current => _temp;

    /// <summary>累加一段临时上限（银龙血统掉血转化时调用）。</summary>
    internal static void Add(decimal amount)
    {
        if (amount <= 0) return;
        _temp += amount;
    }

    /// <summary>
    ///     消耗至多 <paramref name="amount" /> 点临时上限（浴血涅槃用），
    ///     返回**实际消耗掉的数量**（池子不足时按池子的量算）。
    /// </summary>
    internal static decimal Consume(decimal amount)
    {
        if (amount <= 0 || _temp <= 0) return 0;
        var used = Math.Min(amount, _temp);
        _temp -= used;
        return used;
    }

    /// <summary>清空（战斗结束、遗物已把上限还原之后调用）。</summary>
    internal static void Reset() => _temp = 0;
}