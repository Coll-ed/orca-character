using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **烬血之翼**（2 费 · 技能 · 稀有 · 己方）—— 获得 1 层【翱翔】。
///
///     <para>【翱翔】= **自写的 <see cref="OrcaSoarPower" />**（受到的伤害减半；每受到一次未格挡伤害减一层）。
///     不用原版 <c>SoarPower</c> 的原因：它是 <c>sealed</c> + <c>PowerStackType.Single</c>
///     ⇒ 既不能继承、也不能减层。</para>
///
///     <para>基础版额外挂一个"回合开始摘除"的记号（<see cref="OrcaEmberWingPower" />）；
///     敲后不挂 ⇒ 翱翔不再因回合开始而消失（用户口径）。</para>
///
///     <para><b>本文件是「重建源码树」的第 8 个文件</b>。改写前是反编译直出（并经过一次手改），
///     含 <c>(CardModel)(object)this</c> 强转与数字化枚举。</para>
/// </summary>
public sealed class OrcaEmberWing : OrcaCard
{
    /// <summary>施加的【翱翔】层数。</summary>
    private const int SoarStacks = 1;

    /// <summary>施加的记号层数（记号不叠层，恒为 1）。</summary>
    private const int MarkerStacks = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    public OrcaEmberWing()
        : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner.Creature;

        // 自写翱翔（可减层；见类注释）
        await PowerCmd.Apply<OrcaSoarPower>(ctx, creature, SoarStacks, creature, this, false);

        // 基础版才挂"回合开始摘除"记号；敲后不挂 ⇒ 翱翔不再因回合开始消失
        if (!IsUpgraded)
        {
            await PowerCmd.Apply<OrcaEmberWingPower>(ctx, creature, MarkerStacks, creature, this, false);
        }

        OrcaLog.Info("[Orca] 烬血之翼：获得 1 层【翱翔】（受到的伤害减半），"
                  + (IsUpgraded ? "敲后·回合开始不再消失（挨未格挡伤害减一层）" : "基础·回合开始时消失（挨未格挡伤害减一层）"));
    }

    /// <summary>
    ///     只补一句"什么时候消失"的说明 —— 减伤数值与减层规则由【翱翔】词条自己描述
    ///     （<c>OrcaKeyword.Soar</c>），避免同一件事在两处维护。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add("Expire", IsUpgraded ? "\n回合开始时不再消失。" : "\n你的回合开始时，翱翔消失。");
    }

    /// <summary>敲后不加数值，只改变"翱翔是否因回合开始消失"的行为（见 <see cref="OnPlay" />）。</summary>
    protected override void OnUpgrade()
    {
    }
}
