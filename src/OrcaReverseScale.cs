using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace OrcaCharacter;

public sealed class OrcaReverseScale : OrcaCard
{
	private const int RegenPercent = 20;

	private const int DrawToFull = 10;

	public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

	public override IEnumerable<CardKeyword> CanonicalKeywords => (IEnumerable<CardKeyword>)(object)new CardKeyword[2]
	{
		(CardKeyword)1,
		(CardKeyword)2
	};

	private int CurrentRegen
	{
		get
		{
			try
			{
				Player owner = ((CardModel)this).Owner;
				Creature val = ((owner != null) ? owner.Creature : null);
				if (val == null || val.CurrentHp <= 1)
				{
					return 0;
				}
				return (int)Math.Floor((double)((val.CurrentHp - 1) * 20) / 100.0);
			}
			catch
			{
				return 0;
			}
		}
	}

	public OrcaReverseScale()
		: base(3, (CardType)1, (CardRarity)4, (TargetType)1)
	{
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		description.Add(new DynamicVar("RegenPercent", 20m));
		description.Add(new DynamicVar("Regen", (decimal)CurrentRegen));
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		Creature me = ((CardModel)this).Owner.Creature;
		int lost = Math.Max(0, me.CurrentHp - 1);
		if (lost > 0)
		{
			await CreatureCmd.SetCurrentHp(me, 1m);
		}
		int regen = (int)Math.Floor((double)(lost * 20) / 100.0);
		if (regen > 0)
		{
			await PowerCmd.Apply<RegenPower>(ctx, me, (decimal)regen, me, (CardModel)(object)this, false);
		}
		PlayerCombatState playerCombatState = ((CardModel)this).Owner.PlayerCombatState;
		int maxEnergy = ((playerCombatState != null) ? playerCombatState.MaxEnergy : 3);
		await PlayerCmd.SetEnergy((decimal)maxEnergy, ((CardModel)this).Owner);
		await CardPileCmd.Draw(ctx, 10m, ((CardModel)this).Owner, false);
		await PowerCmd.Apply<OrcaExtraTurnPower>(ctx, me, 1m, me, (CardModel)(object)this, false);
		OrcaLog.Info($"[Orca] 逆鳞：失去 {lost} 点生命（→1）⇒ 获得 {regen} 点再生（{20}%）；能量补满至 {maxEnergy}、抽 {10} 张、并获得一个额外回合");
	}

	protected override void OnUpgrade()
	{
		CardCmd.RemoveKeyword((CardModel)(object)this, (CardKeyword[])(object)new CardKeyword[1] { (CardKeyword)2 });
	}
}
