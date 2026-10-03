using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

public sealed class OrcaDefend : OrcaCard
{
	public override bool GainsBlock => true;

	protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { (CardTag)2 };

	protected override IEnumerable<DynamicVar> CanonicalVars => (IEnumerable<DynamicVar>)(object)new BlockVar[1]
	{
		new BlockVar(5m, (ValueProp)8)
	};

	public OrcaDefend()
		: base(1, (CardType)2, (CardRarity)1, (TargetType)1)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		OrcaLog.Info($"[Orca] 防御效果：获得 {((DynamicVar)((CardModel)this).DynamicVars.Block).BaseValue} 点格挡");
		await CreatureCmd.GainBlock(((CardModel)this).Owner.Creature, ((CardModel)this).DynamicVars.Block, play, false);
	}

	protected override void OnUpgrade()
	{
		((DynamicVar)((CardModel)this).DynamicVars.Block).UpgradeValueBy(3m);
	}
}
