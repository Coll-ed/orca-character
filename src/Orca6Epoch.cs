using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

public sealed class Orca6Epoch : EpochModel
{
	public override string Id => "ORCA6_EPOCH";

	public override EpochEra Era => (EpochEra)1803;

	public override int EraPosition => 1;

	public override string? StoryId => null;

	public override string UnlockText => "解锁了银龙奥卡的一段回忆。";

	public override void QueueUnlocks()
	{
		OrcaLog.Info("[Orca] ORCA6_EPOCH 解锁（击败 15 个 Boss）—— 暂无专属解锁内容");
	}
}
