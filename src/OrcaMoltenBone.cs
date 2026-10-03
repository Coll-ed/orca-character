using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

public sealed class OrcaMoltenBone : OrcaCard
{
	public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

	public OrcaMoltenBone()
		: base(3, (CardType)3, (CardRarity)4, (TargetType)1)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		await PowerCmd.Apply<OrcaMoltenBonePower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		OrcaLog.Info("[Orca] 熔渊枯骨：获得【生死一线】—— 焚烧只消耗一半层数，且波及场上所有人（含自己）");
	}

	protected override void OnUpgrade()
	{
		CardCmd.ApplyKeyword((CardModel)(object)this, (CardKeyword[])(object)new CardKeyword[1] { (CardKeyword)3 });
	}
}
