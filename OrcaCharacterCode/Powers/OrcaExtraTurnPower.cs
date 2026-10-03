using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

public sealed class OrcaExtraTurnPower : PowerModel
{
	public override PowerType Type => (PowerType)1;

	public override PowerStackType StackType => (PowerStackType)0;

	public override bool ShouldTakeExtraTurn(Player player)
	{
		Creature owner = ((PowerModel)this).Owner;
		return player == ((owner != null) ? owner.Player : null);
	}

	public override async Task AfterTakingExtraTurn(Player player)
	{
		Creature owner = ((PowerModel)this).Owner;
		if (player == ((owner != null) ? owner.Player : null))
		{
			await PowerCmd.Remove((PowerModel)(object)this);
		}
	}
}
