using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **睥睨的三个专属 buff**（用户口径 2026-09-23 定稿）：
///     <i>「每种种类的卡牌，可以额外打一次，也就是有 **3 种机会**，这样才对得起别人必须在
///     BOSS 战奖励才能获得的强度。实现：单独注册 3 个与原版一模一样代码的 buff，
///     但是其名字改变为、图标为深红色原版 ——
///     【杀意】额外攻击 1 次，【智慧】额外技能，【血统】额外能力」</i>
///
///     <para><b>为什么不用原版三件套</b>：原版【连环拳】【爆发】【信号增强】是**别人角色的东西**，
///     直接用会让奥卡顶着别人的名字和图标；而且我们**无法只给奥卡染色**（图集是全局的，
///     染色会污染其他角色 —— 与之前修掉的"格挡音泄漏"同类问题）。
///     所以**自己注册 3 个**：代码逻辑与原版完全一致，名字换成奥卡的，图标用**深红色版原版图**。</para>
///
///     <para><b>代码挂点</b>照抄原版 <c>DuplicationPower</c> / <c>OneTwoPunchPower</c> 那一套：
///     <c>ModifyCardPlayCount</c> 对本牌型的牌返回 <c>playCount + 1</c>，
///     配对钩子 <c>AfterModifyingCardPlayCount</c> 在**确实触发的那一次**减 1 层
///     （因此必须再加一道"牌型对不对"的判定，否则别的牌型打出来也会误扣层数）。</para>
/// </summary>
internal static class OrcaDuplicationHelper
{
    /// <summary>
    ///     通用的"某牌型下 1 张多打出一次"判定。
    ///     <list type="bullet">
    ///       <item>必须是**自己**（Owner）打出的牌；</item>
    ///       <item>必须是**指定牌型**（<c>CardType</c>：Attack=1 / Skill=2 / Power=3）。</item>
    ///     </list>
    /// </summary>
    internal static bool ShouldBoost(Creature? owner, CardModel? card, CardType wanted)
    {
        try
        {
            if (owner == null || card == null) return false;
            if (card.Owner?.Creature != owner) return false;
            return card.Type == wanted;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>★【杀意】—— 下 1 张**攻击牌**多打出一次（深红版原版图标）。</summary>
public sealed class OrcaKillingIntentPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
        => OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Attack) ? playCount + 1 : playCount;

    public override async Task AfterModifyingCardPlayCount(CardModel card)
    {
        if (!OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Attack)) return;
        await PowerCmd.Decrement(this);
    }
}

/// <summary>★【智慧】—— 下 1 张**技能牌**多打出一次。</summary>
public sealed class OrcaWisdomPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
        => OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Skill) ? playCount + 1 : playCount;

    public override async Task AfterModifyingCardPlayCount(CardModel card)
    {
        if (!OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Skill)) return;
        await PowerCmd.Decrement(this);
    }
}

/// <summary>★【血统】—— 下 1 张**能力牌**多打出一次。</summary>
public sealed class OrcaLineagePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
        => OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Power) ? playCount + 1 : playCount;

    public override async Task AfterModifyingCardPlayCount(CardModel card)
    {
        if (!OrcaDuplicationHelper.ShouldBoost(Owner, card, CardType.Power)) return;
        await PowerCmd.Decrement(this);
    }
}