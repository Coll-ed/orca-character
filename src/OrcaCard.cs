using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

public abstract class OrcaCard : CardModel
{
	private bool _viaCodex;

	public override CardPoolModel Pool => (CardPoolModel)(object)ModelDb.CardPool<OrcaCardPool>();

	public virtual OrcaOrbForm OrbForm => OrcaOrbForm.None;

	private string AssetEntry
	{
		get
		{
			string entry = ((AbstractModel)this).Id.Entry;
			if (!entry.StartsWith("ORCA_", StringComparison.Ordinal))
			{
				return entry.ToLowerInvariant();
			}
			string text = entry;
			int length = "ORCA_".Length;
			return text.Substring(length, text.Length - length).ToLowerInvariant();
		}
	}

	public override string PortraitPath => "res://images/packed/card_portraits/orca/" + AssetEntry + ".png";

	public override string BetaPortraitPath => ((CardModel)this).PortraitPath;

	protected override string PortraitPngPath => ((CardModel)this).PortraitPath;

	public override IEnumerable<string> AllPortraitPaths => new string[1] { ((CardModel)this).PortraitPath };

	internal void MarkViaCodex()
	{
		_viaCodex = true;
	}

	internal void ClearViaCodex()
	{
		_viaCodex = false;
	}

	internal bool ConsumeViaCodex()
	{
		bool viaCodex = _viaCodex;
		_viaCodex = false;
		return viaCodex;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (cardPlay.Card == this)
		{
			Player owner = ((CardModel)this).Owner;
			Creature val = ((owner != null) ? owner.Creature : null);
			if (val != null)
			{
				bool grantsBlock = false;
				try
				{
					grantsBlock = ((CardModel)this).DynamicVars.ContainsKey("Block");
				}
				catch
				{
				}
				OrcaSpeech.SayCardType(val, (int)((CardModel)this).Type, grantsBlock);
			}
			Player owner2 = ((CardModel)this).Owner;
			OrcaSpeech.SayCardLine((owner2 != null) ? owner2.Creature : null, ((AbstractModel)this).Id.Entry);
		}
		await _003C_003En__0(choiceContext, cardPlay);
	}

	internal async Task ReturnToDrawPileTop(string why)
	{
		try
		{
			Player owner = ((CardModel)this).Owner;
			if (owner != null)
			{
				await CardPileCmd.Add((CardModel)(object)this, PileTypeExtensions.GetPile((PileType)1, owner), (CardPilePosition)2, (AbstractModel)null, false);
				OrcaLog.Info($"[Orca] {why}：{((AbstractModel)this).Id.Entry} 回到抽牌堆**第一位**");
			}
		}
		catch (Exception ex)
		{
			OrcaLog.Warn($"[Orca] {why}：回抽牌堆失败（{((AbstractModel)this).Id.Entry}）：{ex.Message}");
		}
	}

	protected OrcaCard(int cost, CardType type, CardRarity rarity, TargetType target, bool showInLibrary = true)
		: base(cost, type, rarity, target, showInLibrary)
	{
	}//IL_0002: Unknown result type (might be due to invalid IL or missing references)
	//IL_0003: Unknown result type (might be due to invalid IL or missing references)
	//IL_0004: Unknown result type (might be due to invalid IL or missing references)


	[CompilerGenerated]
	[DebuggerHidden]
	private Task _003C_003En__0(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return ((AbstractModel)this).AfterCardPlayed(choiceContext, cardPlay);
	}
}
