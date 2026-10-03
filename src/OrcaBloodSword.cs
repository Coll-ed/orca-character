using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace OrcaCharacter;

public sealed class OrcaBloodSword : OrcaFrenzyCard
{
	private int _bonus;

	private int _lifePercent = 20;

	private const int MaxHpPerKill = 2;

	public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

	internal int Bonus => _bonus;

	internal int LifePercent => _lifePercent;

	protected override bool IsPlayable
	{
		get
		{
			try
			{
				Player owner = ((CardModel)this).Owner;
				Creature val = ((owner != null) ? owner.Creature : null);
				if (val == null)
				{
					return true;
				}
				if ((int)Math.Floor((double)(val.MaxHp * _lifePercent) / 100.0) < val.CurrentHp)
				{
					return true;
				}
				OrcaSpeech.SayCapped(val, (VfxDuration)2, "swordRefuse", 1, "ORCA.banter.swordRefuse.1", "ORCA.banter.swordRefuse.2", "ORCA.banter.swordRefuse.3", "ORCA.banter.swordRefuse.4", "ORCA.banter.swordRefuse.5", "ORCA.banter.swordRefuse.6");
				return false;
			}
			catch
			{
				return true;
			}
		}
	}

	internal int MaxHpPerKillValue => 2;

	internal int CurrentLifeCost
	{
		get
		{
			try
			{
				Player owner = ((CardModel)this).Owner;
				Creature val = ((owner != null) ? owner.Creature : null);
				if (val == null || val.MaxHp <= 0)
				{
					return 0;
				}
				int num = (int)Math.Floor((double)(val.MaxHp * _lifePercent) / 100.0);
				int num2 = val.CurrentHp - 1;
				return (num > num2) ? Math.Max(num2, 0) : num;
			}
			catch
			{
				return 0;
			}
		}
	}

	protected override IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

	public override IEnumerable<CardKeyword> CanonicalKeywords => (IEnumerable<CardKeyword>)(object)new CardKeyword[1] { (CardKeyword)7 };

	internal void AddBonus(int amount)
	{
		if (amount > 0)
		{
			_bonus += amount;
			OrcaCardUi.Refresh((CardModel?)(object)this);
		}
	}

	public OrcaBloodSword()
		: base(2, (CardType)2, (CardRarity)4, (TargetType)1)
	{
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Expected O, but got Unknown
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected O, but got Unknown
		int currentLifeCost = CurrentLifeCost;
		int num = (currentLifeCost + 1) / 2;
		description.Add(new DynamicVar("LifeLoss", (decimal)currentLifeCost));
		description.Add(new DynamicVar("HpLoss", (decimal)currentLifeCost));
		description.Add(new DynamicVar("Gain", (decimal)num));
		description.Add(new DynamicVar("LifePercent", (decimal)_lifePercent));
		description.Add(new DynamicVar("Bonus", (decimal)_bonus));
		description.Add(new DynamicVar("Dealt", (decimal)_bonus));
		description.Add(new DynamicVar("MaxHpPerKill", 2m));
		description.Add(new DynamicVar("LifestealPercent", 25m));
		description.Add("KeywordTags", "[gold]" + OrcaKeyword.Rampage.Title + "[/gold]。\n[gold]永恒[/gold]。");
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		if (IsFrenzyPlay(play))
		{
			await OnFrenzyPlay(ctx, play);
			return;
		}
		if (play.IsAutoPlay)
		{
			OrcaLog.Info("[Orca] 嗜血龙剑：本次由**龙族魔典**打出 → 走正常使用的强化（不是狂躁的全体伤害）");
		}
		Creature creature = ((CardModel)this).Owner.Creature;
		int lifeLoss = (int)Math.Floor((double)(creature.MaxHp * _lifePercent) / 100.0);
		int num = creature.CurrentHp - 1;
		if (lifeLoss > num)
		{
			OrcaLog.Info($"[Orca] 嗜血龙剑：代价 {lifeLoss} 超过当前生命可支付上限 {num} ⇒ 钳到 {Math.Max(num, 0)}");
			lifeLoss = Math.Max(num, 0);
		}
		if (lifeLoss > 0)
		{
			await CreatureCmd.SetCurrentHp(creature, (decimal)(creature.CurrentHp - lifeLoss));
			int num2 = (int)Math.Ceiling((double)lifeLoss * 0.5);
			_bonus += num2;
			OrcaLog.Info($"[Orca] 嗜血龙剑·主动打出：失去生命 {lifeLoss}（{_lifePercent}% × 最大上限 {creature.MaxHp}）→ 伤害附加 +{num2}（累计 {_bonus}），本次不造成伤害");
			OrcaCardUi.Refresh((CardModel?)(object)this);
		}
		else
		{
			OrcaLog.Info("[Orca] 嗜血龙剑·主动打出：当前生命不足以支付代价（失去 0），伤害附加不变");
		}
	}

	protected override async Task OnFrenzyPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		int bonus = _bonus;
		ICombatState combatState = ((CardModel)this).Owner.Creature.CombatState;
		if (combatState == null)
		{
			OrcaLog.Warn("[Orca] 狂躁：不在战斗中，本次不结算");
			return;
		}
		OrcaLog.Info($"[Orca] 嗜血龙剑·狂躁（回合结束自动打出）：对**全体敌人**造成 {bonus} 点伤害（伤害附加 {_bonus}）");
		if (bonus <= 0)
		{
			OrcaLog.Info("[Orca] 狂躁：伤害附加为 0 ⇒ 正常进弃牌堆（不回抽牌堆）");
			return;
		}
		try
		{
			AttackCommand attack = DamageCmd.Attack((decimal)bonus).FromCard((CardModel)(object)this, play).TargetingAllOpponents(combatState)
				.WithHitFx("vfx/vfx_attack_slash", (string)null, (string)null);
			int value = combatState.Enemies.Count((Creature e) => !e.IsDead);
			OrcaLog.Info($"[Orca] 狂躁结算：攻击者={((CardModel)this).Owner.Creature.Name}、存活敌人 {value} 个、伤害 {bonus}");
			await attack.Execute(ctx);
			List<DamageResult> list = attack.Results.SelectMany((List<DamageResult> r) => r).ToList();
			string value2 = string.Join("、", list.Select(delegate(DamageResult r)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(1, 2);
				Creature receiver2 = r.Receiver;
				defaultInterpolatedStringHandler2.AppendFormatted((receiver2 != null) ? receiver2.Name : null);
				defaultInterpolatedStringHandler2.AppendLiteral(":");
				defaultInterpolatedStringHandler2.AppendFormatted(r.TotalDamage);
				return defaultInterpolatedStringHandler2.ToStringAndClear();
			}));
			OrcaLog.Info($"[Orca] 狂躁结算完成：命中 {list.Count} 次，合计 {list.Sum((DamageResult r) => r.TotalDamage)} 点伤害（{value2}）");
			OrcaLog.Info($"[Orca] 狂调结算明细：Results 组数={attack.Results.Count()}、展平后 {list.Count} 条 → " + string.Join(" | ", list.Select(delegate(DamageResult r)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 3);
				Creature receiver = r.Receiver;
				defaultInterpolatedStringHandler.AppendFormatted((receiver != null) ? receiver.Name : null);
				defaultInterpolatedStringHandler.AppendLiteral("(伤");
				defaultInterpolatedStringHandler.AppendFormatted(r.TotalDamage);
				defaultInterpolatedStringHandler.AppendLiteral(",杀=");
				defaultInterpolatedStringHandler.AppendFormatted(r.WasTargetKilled ? 1 : 0);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				return defaultInterpolatedStringHandler.ToStringAndClear();
			})));
			int killed = list.Count((DamageResult r) => r.WasTargetKilled);
			if (killed > 0)
			{
				int gain = killed * 2;
				await CreatureCmd.GainMaxHp(((CardModel)this).Owner.Creature, (decimal)gain);
				OrcaLog.Info($"[Orca] 嗜血龙剑击杀 {killed} 个敌人 → 生命上限 +{gain}（每个 +{2}）");
			}
		}
		catch (Exception value3)
		{
			Log.Error($"[Orca] 狂躁结算抛异常（本次没造成伤害）：{value3}", 2);
		}
	}

	protected override void OnUpgrade()
	{
		_lifePercent = 30;
		CardCmd.ApplyKeyword((CardModel)(object)this, (CardKeyword[])(object)new CardKeyword[1] { (CardKeyword)3 });
	}
}
