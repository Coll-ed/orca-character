using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

public sealed class Orca3Epoch : EpochModel
{
	public override string Id => "ORCA3_EPOCH";

	public override EpochEra Era => (EpochEra)3;

	public override int EraPosition => 0;

	public override string? StoryId => null;

	public override string UnlockText => "解锁了银龙奥卡的一段回忆。";

	public override void QueueUnlocks()
	{
	}
}
