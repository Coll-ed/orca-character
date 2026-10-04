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
		// ★ 改用【自写】翱翔：原版 SoarPower 是 sealed + StackType.Single ⇒ 不能继承、也不能减层。
		await PowerCmd.Apply<OrcaSoarPower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		// 基础版额外挂"回合开始摘除"记号；敲后不挂（= 敲后只为"不消失"，与用户口径一致）。
		if (!((CardModel)this).IsUpgraded)
		{
			await PowerCmd.Apply<OrcaEmberWingPower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		}
		OrcaLog.Info("[Orca] 烬血之翼：获得 1 层【翱翔】（受到的伤害减半），" + (((CardModel)this).IsUpgraded ? "敲后·回合开始不再消失（挨未格挡伤害减一层）" : "基础·回合开始时消失（挨未格挡伤害减一层）"));
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		description.Add("Expire", ((CardModel)this).IsUpgraded ? "\n回合开始时不再消失。" : "\n你的回合开始时，翱翔消失。");
	}

	protected override void OnUpgrade()
	{
	}
}
