using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **额外回合**（一次性 buff，由【逆鳞】施加）。
///
///     <para>存在期间允许拥有者多打一个回合；**用掉即自己移除**（纯标记，不叠层、不显示层数）。</para>
///
///     <para><b>本文件是「重建源码树」的第 7 个文件</b>。改写前是反编译直出，含
///     <c>(PowerModel)this</c> / <c>(PowerModel)(object)this</c> 强转，以及被数字化成
///     <c>(PowerType)1</c> / <c>(PowerStackType)0</c> 的枚举。</para>
///
///     <para>★ 枚举已查证：<c>PowerType.Buff=1</c>、
///     <c>PowerStackType.None=0</c>（<c>None/Counter/Single</c> 三值 —— None = 不叠层、不显示层数，
///     正是"一次性标记"该用的值 ✓）。</para>
/// </summary>
public sealed class OrcaExtraTurnPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>纯标记：不叠层、不显示层数。</summary>
    public override PowerStackType StackType => PowerStackType.None;

    /// <summary>
    ///     轮到拥有者时，允许他多拿一个回合。
    ///     ★ 写成 <c>player == Owner?.Player</c> 是**严格等价**于原反编译式
    ///     <c>player == ((owner != null) ? owner.Player : null)</c> —— 含 <c>player == null</c> 的边界也一致，
    ///     故不额外加 <c>player != null</c> 判断（那会改变边界行为）。
    /// </summary>
    public override bool ShouldTakeExtraTurn(Player player) => player == Owner?.Player;

    /// <summary>额外回合用掉之后，自己消失（一次性）。</summary>
    public override async Task AfterTakingExtraTurn(Player player)
    {
        if (player == Owner?.Player)
        {
            await PowerCmd.Remove(this);
        }
    }
}
