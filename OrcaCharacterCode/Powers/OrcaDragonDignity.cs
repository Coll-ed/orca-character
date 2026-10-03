using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace OrcaCharacter;

public sealed class OrcaDragonDignity : OrcaCard
{
	private int _stacks = 1;

	public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

	public override IEnumerable<CardKeyword> CanonicalKeywords => (IEnumerable<CardKeyword>)(object)new CardKeyword[1] { (CardKeyword)1 };

	public OrcaDragonDignity()
		: base(1, (CardType)2, (CardRarity)3, (TargetType)1)
	{
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected O, but got Unknown
		description.Add(new DynamicVar("Stacks", (decimal)_stacks));
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		ICombatState combatState = ((CardModel)this).Owner.Creature.CombatState;
		int hit = 0;
		if (combatState != null)
		{
			foreach (Creature enemy in combatState.Enemies.Where((Creature e) => !e.IsDead).ToList())
			{
				await PowerCmd.Apply<VulnerablePower>(ctx, enemy, (decimal)_stacks, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
				await PowerCmd.Apply<WeakPower>(ctx, enemy, (decimal)_stacks, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
				hit++;
			}
		}
		OrcaLog.Info($"[Orca] 龙之威仪：给 {hit} 个敌人各 {_stacks} 层易伤 + {_stacks} 层虚弱");
	}

	protected override void OnUpgrade()
	{
		_stacks = 2;
	}
}
