using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

public sealed class OrcaDragonCodex : OrcaCard
{
	public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

	private static LocString CodexPrompt
	{
		get
		{
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Expected O, but got Unknown
			try
			{
				LocString val = new LocString("cards", "ORCA_DRAGON_CODEX.pick");
				val.Add("Amount", 1m);
				return val;
			}
			catch (Exception ex)
			{
				OrcaLog.Warn("[Orca] 魔典选择提示语取不到，退回原版提示：" + ex.Message);
				return CardSelectorPrefs.UpgradeSelectionPrompt;
			}
		}
	}

	public OrcaDragonCodex()
		: base(1, (CardType)2, (CardRarity)2, (TargetType)1)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		CardPlay play2 = play;
		Player owner = ((CardModel)this).Owner;
		CardSelectorPrefs val = new CardSelectorPrefs(CodexPrompt, 1)
		{
			Cancelable = true,
			PretendCardsCanBePlayed = true
		};
		CardModel card = (await CardSelectCmd.FromHand(ctx, owner, val, (Func<CardModel, bool>)((CardModel c) => c != play2.Card && !c.Keywords.Contains((CardKeyword)4)), (AbstractModel)(object)this))?.FirstOrDefault();
		if (card == null)
		{
			OrcaLog.Info("[Orca] 龙族魔典：没有选中任何牌（手牌为空或玩家取消）");
			return;
		}
		if (card is OrcaCard orcaCard && orcaCard.OrbForm == OrcaOrbForm.Sword)
		{
			Creature creature = ((CardModel)this).Owner.Creature;
			try
			{
				await PowerCmd.Apply<OrcaLifestealPower>(ctx, creature, 1m, creature, (CardModel)(object)this, false);
				OrcaLog.Info("[Orca] 龙族魔典：选中 " + ((AbstractModel)card).Id.Entry + " 带【魔剑】标签 → 吸血 +1 层");
			}
			catch (Exception ex)
			{
				OrcaLog.Warn("[Orca] 龙族魔典：叠吸血失败（不影响打出）：" + ex.Message);
			}
		}
		card.EnergyCost.SetUntilPlayed(0, false);
		Creature target = ResolveTarget(card);
		int extra = 0;
		try
		{
			OrcaCodexEchoPower power = ((CardModel)this).Owner.Creature.GetPower<OrcaCodexEchoPower>();
			if (power != null && ((PowerModel)power).Amount > 0)
			{
				int left2 = ((PowerModel)power).Amount - 1;
				extra = 1;
				await PowerCmd.Decrement((PowerModel)(object)power);
				OrcaLog.Info($"[Orca] 龙族魔典：消费【回响】1 层 → 本次额外打出 1 次（剩余 {left2} 层）");
			}
		}
		catch (Exception ex2)
		{
			OrcaLog.Warn("[Orca] 龙族魔典：读【回响】失败（按 1 次打）：" + ex2.Message);
			extra = 0;
		}
		int times = 1 + extra;
		for (int left2 = 0; left2 < times; left2++)
		{
			if (card is OrcaCard orcaCard2)
			{
				orcaCard2.MarkViaCodex();
			}
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[Orca] 龙族魔典：正常打出 ");
			defaultInterpolatedStringHandler.AppendFormatted(((AbstractModel)card).Id.Entry);
			defaultInterpolatedStringHandler.AppendLiteral("（第 ");
			defaultInterpolatedStringHandler.AppendFormatted(left2 + 1);
			defaultInterpolatedStringHandler.AppendLiteral("/");
			defaultInterpolatedStringHandler.AppendFormatted(times);
			defaultInterpolatedStringHandler.AppendLiteral(" 次，费用已改 0、目标=");
			defaultInterpolatedStringHandler.AppendFormatted(((target != null) ? target.Name : null) ?? "自动");
			defaultInterpolatedStringHandler.AppendLiteral("）");
			OrcaLog.Info(defaultInterpolatedStringHandler.ToStringAndClear());
			try
			{
				await CardCmd.AutoPlay(ctx, card, target, (AutoPlayType)1, false, false);
			}
			finally
			{
				if (card is OrcaCard orcaCard3)
				{
					orcaCard3.ClearViaCodex();
				}
			}
		}
	}

	private Creature? ResolveTarget(CardModel card)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Invalid comparison between Unknown and I4
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Invalid comparison between Unknown and I4
		try
		{
			ICombatState combatState = ((CardModel)this).Owner.Creature.CombatState;
			if (combatState == null)
			{
				return null;
			}
			TargetType targetType = card.TargetType;
			if ((int)targetType != 2)
			{
				if ((int)targetType == 6)
				{
					foreach (Creature ally in combatState.Allies)
					{
						if (ally != null && ally.IsAlive && ally.IsPlayer && ally != ((CardModel)this).Owner.Creature)
						{
							return ally;
						}
					}
					return null;
				}
				return null;
			}
			foreach (Creature enemy in combatState.Enemies)
			{
				if (!enemy.IsDead)
				{
					return enemy;
				}
			}
			return null;
		}
		catch (Exception ex)
		{
			OrcaLog.Warn("[Orca] 魔典解析目标失败（交给 AutoPlay 随机）：" + ex.Message);
			return null;
		}
	}

	protected override void OnUpgrade()
	{
		CardCmd.ApplyKeyword((CardModel)(object)this, (CardKeyword[])(object)new CardKeyword[1] { (CardKeyword)5 });
	}
}
