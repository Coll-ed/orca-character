using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

public sealed class Orca2Epoch : EpochModel
{
	public override string Id => "ORCA2_EPOCH";

	public override EpochEra Era => (EpochEra)1202;

	public override int EraPosition => 1;

	public override string? StoryId => null;

	public override string UnlockText => "解锁了银龙奥卡的一段回忆。";

	public override void QueueUnlocks()
	{
	}
}
