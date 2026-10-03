using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace OrcaCharacter;

public sealed class OrcaEmberWing : OrcaCard
{
	public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

	public OrcaEmberWing()
		: base(2, (CardType)2, (CardRarity)4, (TargetType)1)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		await PowerCmd.Apply<SoarPower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		// 烬血之翼：获得 1 层【翱翔】（40%→50% 减伤由原版 SoarPower 提供）。
		// 基础版额外挂"回合开始摘除"记号；敲后不挂（= 敲后只为"不消失"，与用户口径一致）。
		if (!((CardModel)this).IsUpgraded)
		{
			await PowerCmd.Apply<OrcaEmberWingPower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		}
		OrcaLog.Info("[Orca] 烬血之翼：获得 1 层【翱翔】（受到攻击伤害减半），" + (((CardModel)this).IsUpgraded ? "敲后·仅挨未格挡伤害时消失（回合开始不再消失）" : "基础·挨未格挡伤害或回合开始时消失"));
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		description.Add("Expire", ((CardModel)this).IsUpgraded ? "\n当你受到敌人未被格挡的伤害时，翱翔消失。" : "\n当你受到敌人未被格挡的伤害，或你的回合开始时，翱翔消失。");
	}

	protected override void OnUpgrade()
	{
	}
}
