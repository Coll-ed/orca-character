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
///     <para>该牌由"选取遗物-欧洛巴斯之触"获得；比例 25%，敲后 50%
///     （敲后的比例通过 <see cref="OrcaHomesteadPower.UpgradedRatio" /> 传递，见 <see cref="OnUpgrade" />）。</para>
///
///     <para><b>本文件是「重建源码树」的第 9 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、两行 <c>//IL_</c> 残留与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Ancient=5</c>（先古 ✓）、
///     <c>TargetType.Self=1</c>。</para>
/// </summary>
public sealed class OrcaHomestead : OrcaCard
{
    /// <summary>基础转化比例（%）。</summary>
    private const int BasePercent = 25;

    /// <summary>敲后转化比例（%）。</summary>
    private const int UpgradedPercent = 50;

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
        await PowerCmd.Apply<OrcaHomesteadPower>(ctx, creature, Stacks, creature, this, false);
        OrcaLog.Info($"[Orca] 栖途：本场战斗结束时，{CurrentPercent}% 的战斗临时生命上限将转化为真实生命上限");
    }

    /// <summary>当前比例（敲后 50%，否则 25%）—— 卡面数值与日志共用此口径。</summary>
    private int CurrentPercent => IsUpgraded ? UpgradedPercent : BasePercent;

    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("Percent", (decimal)CurrentPercent));
    }

    /// <summary>敲后：比例 25% → 50%（Power 侧读这个开关，单一来源）。</summary>
    protected override void OnUpgrade() => OrcaHomesteadPower.UpgradedRatio = true;
}
