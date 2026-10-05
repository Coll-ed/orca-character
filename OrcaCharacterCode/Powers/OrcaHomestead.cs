using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **栖途**（3 费 · 能力 · 先古 · 己方）—— 战斗结束后，把一部分**战斗临时生命上限**转化为真实生命上限。
///
///     <para>该牌由"选取遗物-欧洛巴斯之触"获得；比例 25%，敲后 50%。
///     ★ <b>"敲没敲"由卡牌自身状态（<see cref="MegaCrit.Sts2.Core.Models.CardModel.IsUpgraded" />）推导</b>：
///     每次出牌时把本局的比例写进**该 Power 实例**（同 <c>OrcaVoidReturn.OnPlay</c> 的写法），
///     卡面数字、日志、结算三者共用 <see cref="CurrentPercent" /> 一个来源。</para>
///
///     <para>⚠️ <b>E4 已修</b>：这里原来写的是
///     <c>protected override void OnUpgrade() =&gt; OrcaHomesteadPower.UpgradedRatio = true;</c> ——
///     一个**只置位、全工程无复位**的 static 标志，被 <c>OrcaHomesteadPower</c> 的 static
///     <c>Ratio</c> 读走 ⇒ 跨局共享 ⇒ 上一局敲过栖途，下一局开局就按敲后的 50% 结算 ✗。
///     <c>OnUpgrade()</c> 的调用时机（引擎在**升级完成那一刻**回调一次，此后该卡在**整个进程**里
///     都是升级态）决定了：**同一个 static 标志不可能表达"这一局"**。故不再依赖 static。</para>
///
///     <para><b>本文件是「重建源码树」的第 9 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、两行 <c>//IL_</c> 残留与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Ancient=5</c>（先古 ✓）、
///     <c>TargetType.Self=1</c>。</para>
/// </summary>
public sealed class OrcaHomestead : OrcaCard
{
    /// <summary>施加的 Power 层数（该 Power 不叠层，恒为 1）。</summary>
    private const int Stacks = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    public OrcaHomestead()
        : base(3, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner.Creature;
        var power = await PowerCmd.Apply<OrcaHomesteadPower>(ctx, creature, Stacks, creature, this, false);

        // ★★ E4 的修法就在这里：**每次出牌**按本局卡牌状态写一次比例。
        //    Power 实例随 Apply 新建 ⇒ 比例天生是本局的，不存在跨局残留。
        //    写入点与卡面/日志共用 CurrentPercent ⇒ 三处不可能脱钩（单一来源）。
        //    Apply 返回 null（理论上：层数为 0 时引擎不施加）⇒ 记 Warn，不静默。
        if (power != null)
        {
            power.SetRatio(CurrentPercent);
        }
        else
        {
            OrcaLog.Warn($"[Orca] 栖途：出牌但引擎未返回 Power 实例 ⇒ 本场比例未写入（将按默认 25% 结算）");
        }

        OrcaLog.Info($"[Orca] 栖途：本场战斗结束时，{CurrentPercent}% 的战斗临时生命上限将转化为真实生命上限");
    }

    /// <summary>
    ///     当前比例（敲后 50%，否则 25%）—— 卡面数值、日志与 Power 侧的结算共用此口径。
    ///
    ///     <para>★ 那两个百分点数值**定义在 <see cref="OrcaHomesteadPower" /> 里**，这里只**引用**
    ///     （纪律：同一个常量只定义一次 —— 本卡牌侧不再各留一份副本）。</para>
    /// </summary>
    private int CurrentPercent => IsUpgraded ? OrcaHomesteadPower.UpgradedPercent : OrcaHomesteadPower.BasePercent;

    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("Percent", (decimal)CurrentPercent));
    }

    /// <summary>
    ///     敲后：比例 25% → 50%。
    ///
    ///     <para>⚠️ 这里**刻意什么都不写**（与 <c>OrcaVoidReturn.OnUpgrade</c> 逐字同构）：
    ///     升级态是卡牌自己的持久状态，结算时由 <see cref="CurrentPercent" /> 在**每次出牌**现算 ——
    ///     所以本方法不需要、也不可以把"敲过了"记到任何 static 上（那正是 E4 跨局泄漏的成因）。</para>
    /// </summary>
    protected override void OnUpgrade() { }
}
