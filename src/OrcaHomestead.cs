using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

public sealed class OrcaHomestead : OrcaCard
{
	public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

	public OrcaHomestead()
		: base(3, (CardType)3, (CardRarity)5, (TargetType)1)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
	{
		await PowerCmd.Apply<OrcaHomesteadPower>(ctx, ((CardModel)this).Owner.Creature, 1m, ((CardModel)this).Owner.Creature, (CardModel)(object)this, false);
		OrcaLog.Info("[Orca] 栖途：本场战斗结束时，25% 的战斗临时生命上限将转化为真实生命上限");
	}

	protected override void AddExtraArgsToDescription(LocString description)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		description.Add(new DynamicVar("Percent", (decimal)(((CardModel)this).IsUpgraded ? 50 : 25)));
	}

	protected override void OnUpgrade()
	{
		OrcaHomesteadPower.UpgradedRatio = true;
	}
}
