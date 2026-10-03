using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

public sealed class Orca5Epoch : EpochModel
{
	public override string Id => "ORCA5_EPOCH";

	public override EpochEra Era => (EpochEra)1203;

	public override int EraPosition => 3;

	public override string? StoryId => null;

	public override string UnlockText => "解锁了银龙奥卡的一段回忆。";

	public override void QueueUnlocks()
	{
		OrcaLog.Info("[Orca] ORCA5_EPOCH 解锁（击败 15 个精英）—— 暂无专属解锁内容");
	}
}
