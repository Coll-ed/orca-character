using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace OrcaCharacter;

/// <summary>
///     给原版 <see cref="NParticlesContainer" /> 兜底（**只做空引用保护，不改行为**）。
///
///     反编译原版看到的事实：
///     <code>
///     [Export] private Array&lt;GpuParticles2D&gt;? _particles;      // ← 由场景里那一行 _particles = [...] 填
///     public void Restart()      { for (int i = 0; i &lt; _particles.Count; i++) _particles[i].Restart(); }
///     public void SetEmitting(bool e) { for (int i = 0; i &lt; _particles.Count; i++) _particles[i].Emitting = e; }
///     </code>
///     场景里没写 <c>_particles</c> 时字段是 <c>null</c> → 一取 <c>.Count</c> 就空引用。
///     我们自己的奥卡能量球场景就踩了这个坑，日志实锤：
///     <code>
///     [ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck …
///       at NParticlesContainer.Restart()
///       at NEnergyCounter.OnEnergyChanged_Patch1(this, 0, 3)
///       at PlayerCombatState.ResetEnergy_Patch1(this)
///       at CombatManager.SetupPlayerTurn(…)
///     </code>
///     回合 1 补能量 → 空引用 → **整个回合循环死掉 → 开局手牌永远抽不出来**。
///
///     我们的场景已经补上 <c>_particles = []</c>（等价于"这个容器不装粒子"，原版循环自然空转），
///     这里再加一道保险：万一哪个场景（我们的或别的模组的）又是 null，跳过而不是崩掉整场战斗。
/// </summary>
internal static class OrcaParticlesContainerGuard
{
    private static bool _warned;

    /// <summary>字段是不是 null（＝场景没填 <c>_particles</c>）。</summary>
    private static bool IsEmptyShell(NParticlesContainer c)
    {
        try { return Traverse.Create(c).Field("_particles").GetValue() == null; }
        catch { return false; }   // 读不到就当正常，别把原版逻辑拦下来
    }

    private static void WarnOnce(string method)
    {
        if (_warned) return;
        _warned = true;
        OrcaLog.Warn($"[Orca] 拦下一次原版 NParticlesContainer.{method} 的空引用（场景缺 _particles 导出数组）；已跳过，战斗不受影响。", 2);
    }

    [HarmonyPatch(typeof(NParticlesContainer), nameof(NParticlesContainer.Restart))]
    internal static class RestartPatch
    {
        private static bool Prefix(NParticlesContainer __instance)
        {
            if (!IsEmptyShell(__instance)) return true;
            WarnOnce(nameof(NParticlesContainer.Restart));
            return false;
        }
    }

    [HarmonyPatch(typeof(NParticlesContainer), nameof(NParticlesContainer.SetEmitting))]
    internal static class SetEmittingPatch
    {
        private static bool Prefix(NParticlesContainer __instance, bool emitting)
        {
            if (!IsEmptyShell(__instance)) return true;
            WarnOnce($"{nameof(NParticlesContainer.SetEmitting)}({emitting})");
            return false;
        }
    }
}