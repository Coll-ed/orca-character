using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace OrcaCharacter;

/// <summary>
///     击杀敌人后按**房间档位**给的生命上限奖励（普通 / 精英 / BOSS 三档）。
///
///     <para><b>本文件是「重建源码树」的第 12 个文件</b>。改写前是反编译直出，含
///     <c>object obj = …; object obj2 = ((obj is CombatRoom) ? obj : null);</c> 这种
///     **装箱 + 三元式**的机器写法、多行 <c>//IL_</c> 残留、以及数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>RoomType.Elite=2</c>、<c>RoomType.Boss=3</c>、<c>RoomType.Unassigned=0</c>。</para>
///
///     <para>★ 单一来源修正：本类**已有** <see cref="Normal" /> / <see cref="Elite" /> / <see cref="Boss" />
///     三个常量，但 <c>MaxHpFor</c> 与 <c>Describe</c> 里又各自把 2/3/5 写了一遍
///     ⇒ 现全部改用常量（改一处即三处同步）。</para>
/// </summary>
internal static class OrcaKillReward
{
    /// <summary>普通战斗档位的生命上限奖励。</summary>
    internal const int Normal = 2;

    /// <summary>精英档位的生命上限奖励。</summary>
    internal const int Elite = 3;

    /// <summary>BOSS 档位的生命上限奖励。</summary>
    internal const int Boss = 5;

    /// <summary>
    ///     取当前房间档位；**不在战斗房**（含取不到运行时状态、异常）一律按
    ///     <see cref="RoomType.Unassigned" /> 处理 ⇒ 调用方落到"普通档"。
    /// </summary>
    internal static RoomType RoomOf(Player? player)
    {
        try
        {
            if (player?.RunState?.CurrentRoom is CombatRoom room)
            {
                return room.RoomType;
            }

            return RoomType.Unassigned;
        }
        catch
        {
            return RoomType.Unassigned;
        }
    }

    /// <summary>按房间档位给出生命上限奖励（精英 / BOSS 更高，其余按普通档）。</summary>
    internal static int MaxHpFor(Player? player) => RoomOf(player) switch
    {
        RoomType.Elite => Elite,
        RoomType.Boss => Boss,
        _ => Normal,
    };

    /// <summary>日志/浮窗用的档位描述（数值全部取自上面的常量）。</summary>
    internal static string Describe(Player? player) => RoomOf(player) switch
    {
        RoomType.Elite => $"精英档 +{Elite}",
        RoomType.Boss => $"BOSS 档 +{Boss}",
        _ => $"普通档 +{Normal}",
    };
}
