using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace OrcaCharacter;

internal static class OrcaKillReward
{
	internal const int Normal = 2;

	internal const int Elite = 3;

	internal const int Boss = 5;

	internal static RoomType RoomOf(Player? player)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			object obj;
			if (player == null)
			{
				obj = null;
			}
			else
			{
				IRunState runState = player.RunState;
				obj = ((runState != null) ? runState.CurrentRoom : null);
			}
			object obj2 = ((obj is CombatRoom) ? obj : null);
			return (RoomType)((obj2 != null) ? ((int)((AbstractRoom)obj2).RoomType) : 0);
		}
		catch
		{
			return (RoomType)0;
		}
	}

	internal static int MaxHpFor(Player? player)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Invalid comparison between Unknown and I4
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Invalid comparison between Unknown and I4
		RoomType val = RoomOf(player);
		if ((int)val != 2)
		{
			if ((int)val == 3)
			{
				return 5;
			}
			return 2;
		}
		return 3;
	}

	internal static string Describe(Player? player)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Invalid comparison between Unknown and I4
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Invalid comparison between Unknown and I4
		RoomType val = RoomOf(player);
		if ((int)val != 2)
		{
			if ((int)val != 3)
			{
				return $"普通档 +{2}";
			}
			return $"BOSS 档 +{5}";
		}
		return $"精英档 +{3}";
	}
}
