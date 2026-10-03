using MegaCrit.Sts2.Core.Logging;

namespace OrcaCharacter;

/// <summary>
///     ★★ 奥卡的日志出口 —— **全部 <c>[Orca]</c> 日志都从这里走**，由
///     <see cref="OrcaConfig.VerboseLog" />（游戏设置里的「模组配置」开关）统一控制。
///
///     <para>用户口径 2026-09-23：「加一个日志是否开启的功能在设置里面」。</para>
///
///     <para>三条规矩：
///     <list type="number">
///       <item><b>Info / Warn 受开关控制</b> —— 运行时刷屏的就是这两类；</item>
///       <item><b>Error 永远输出</b> —— 真出错不能因为玩家关了日志就查不到；</item>
///       <item>调用形式与原来<b>完全一致</b>（<c>OrcaLog.Info(msg, 2)</c>），
///         所以是全量替换 <c>Log.Info</c> → <c>OrcaLog.Info</c>，没有别的改动。</item>
///     </list></para>
/// </summary>
internal static class OrcaLog
{
    /// <summary>普通信息（默认级别 2，与原来一致）。</summary>
    internal static void Info(string message, int level = 2)
    {
        if (!OrcaConfig.VerboseLog) return;
        Log.Info(message, level);
    }

    /// <summary>警告 —— 同样受开关控制（我们的 Warn 大多是"资源没取到、退回默认"这类噪音）。</summary>
    internal static void Warn(string message, int level = 2)
    {
        if (!OrcaConfig.VerboseLog) return;
        Log.Warn(message, level);
    }

    /// <summary>★ 错误 —— **不受开关控制，永远输出**。</summary>
    internal static void Error(string message, int level = 2) => Log.Error(message, level);
}