using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

public sealed class OrcaStrike : OrcaCard
{
	protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { (CardTag)1 };

	protected override IEnumerable<DynamicVar> CanonicalVars => (IEnumerable<DynamicVar>)(object)new DamageVar[1]
	{
		new DamageVar(6m, (ValueProp)8)
	};

	public OrcaStrike()
		: base(1, (CardType)1, (CardRarity)1, (TargetType)2)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		ArgumentNullException.ThrowIfNull(play.Target, "cardPlay.Target");
		OrcaLog.Info($"[Orca] 打击效果：对 {play.Target.Name} 造成 {((DynamicVar)((CardModel)this).DynamicVars.Damage).BaseValue} 点伤害");
		await DamageCmd.Attack(((DynamicVar)((CardModel)this).DynamicVars.Damage).BaseValue).FromCard((CardModel)(object)this, play).Targeting(play.Target)
			.WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null)
			.Execute(ctx);
	}

	protected override void OnUpgrade()
	{
		((DynamicVar)((CardModel)this).DynamicVars.Damage).UpgradeValueBy(3m);
	}
}
