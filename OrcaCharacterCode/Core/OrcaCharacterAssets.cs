using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace OrcaCharacter;

/// <summary>
///     ★★ 补齐「**原版按角色 id 推导路径、但我们没有对应资源**」的那几项。
///
///     <para>
///     反编译 `CharacterModel` 的实据：几乎所有资源路径都是**按 <c>Id.Entry</c> 拼出来的**：
///     <code>
///     public string AttackSfx  => $"event:/sfx/characters/{Id.Entry.ToLowerInvariant()}/{…}_attack";
///     public string DeathSfx   => $"…/{Id.Entry.ToLowerInvariant()}_die";
///     private string ArmPointingTexturePath => ImageHelper.GetImagePath("ui/hands/multiplayer_hand_" + … + "_point.png");
///     public Achievement RunWonAchievement => Enum.Parse&lt;Achievement&gt;(Id.Entry.Capitalize() + "Win");
///     </code>
///     我们的 id 是 `ORCA` ⇒ 它会去找 <c>event:/sfx/characters/orca/orca_attack</c>、
///     <c>ui/hands/multiplayer_hand_orca_point.png</c>、以及枚举值 <c>OrcaWin</c> —— **三者我们都没有**。
///     </para>
///
///     <para>
///     实机日志实锤（反复刷屏）：
///     <code>
///     cannot find sfx path: event:/sfx/characters/orca/orca_attack
///     cannot find sfx path: event:/sfx/characters/orca/orca_die
///     [WARN] [Audio] FMOD event was not found … path=event:/sfx/characters/orca/orca_die
///     </code>
///     而成就枚举里只有 <c>IroncladWin / SilentWin / RegentWin / NecrobinderWin / DefectWin</c>
///     ⇒ <c>Enum.Parse&lt;Achievement&gt;("OrcaWin")</c> **必抛 ArgumentException**（通关时踩）。
///     </para>
///
///     <para>
///     ⇒ 本文件统一把这些**回退到铁甲战士**：音效借用（挥砍/施法/死亡音是通用音效），
///     猜拳手部贴图借用，通关成就借用 <c>IroncladWin</c>。
///     <b>后面要给奥卡做专属音效/手部贴图时，把这里的 FallbackId 换掉或改成走自己的资源即可。</b>
///     </para>
/// </summary>
internal static class OrcaAssetFallback
{
    /// <summary>回退用的原版角色（资源最齐全的那个）。</summary>
    internal const string Id = "ironclad";

    private static bool _logged;

    internal static void LogOnce(string what)
    {
        if (_logged) return;
        _logged = true;
        OrcaLog.Info($"[Orca] 角色资源回退已启用：{what} → 借用 {Id}"
                 + "（奥卡暂无专属资源；日志里不再刷 'cannot find sfx path'）", 2);
    }
}

// ── ① 三个音效：attack / cast / die ─────────────────────────
//    ★ 2026-09-17 定稿：**从原版 bank 导出铁甲战士的原声 → 变调 → 自备 .tres 播回去**
//      （用户口径："直接导出音效到本地改，然后注册进去啊"）。
//      原声来源：vgmstream 从 `sfx.bank` 按流序号导出（见 `_verify\\make_ironclad_sfx.py` 注释）：
//        attack ← 流#1777 `sts2_sfx_ironclad_attack_blade_v3_rr1`
//        cast   ← 流#623  `sts2_sfx_ironclad_cast_magic_rr1_v1`
//        die    ← 流#1071 `sts2_sfx_ironclad_die_v1`
//      用 `.tres`（AudioStreamWAV 内嵌 PCM）而不是 `.mp3/.wav`：**文本资源不需要导入产物**，
//      绕开 Godot 的 `RSRC`/`.mp3str` 导入格式。播放由 `OrcaAudioPatch` 接管（可设 PitchScale）。
[HarmonyPatch(typeof(CharacterModel), "AttackSfx", MethodType.Getter)]
internal static class OrcaAttackSfxPatch
{
    private static bool Prefix(CharacterModel __instance, ref string __result)
    {
        if (__instance is not Orca) return true;
        __result = "res://OrcaCharacter/audio/orca_attack.tres";
        OrcaAssetFallback.LogOnce("攻击音效（铁甲战士原声变调）");
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), "CastSfx", MethodType.Getter)]
internal static class OrcaCastSfxPatch
{
    private static bool Prefix(CharacterModel __instance, ref string __result)
    {
        if (__instance is not Orca) return true;
        __result = "res://OrcaCharacter/audio/orca_cast.tres";
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), "DeathSfx", MethodType.Getter)]
internal static class OrcaDeathSfxPatch
{
    private static bool Prefix(CharacterModel __instance, ref string __result)
    {
        if (__instance is not Orca) return true;
        __result = "res://OrcaCharacter/audio/orca_die.tres";
        return false;
    }
}

// ── ② 猜拳手部贴图 ×4（多人模式的小游戏）────────────────────
//     patch 的是**返回 Texture2D 的公开属性**（路径属性是 private，没必要去碰）。
[HarmonyPatch(typeof(CharacterModel), "ArmPointingTexture", MethodType.Getter)]
internal static class OrcaArmPointingPatch
{
    private static bool Prefix(CharacterModel __instance, ref Godot.Texture2D __result)
    {
        if (__instance is not Orca) return true;
        __result = ModelDb.Character<Ironclad>().ArmPointingTexture;
        OrcaAssetFallback.LogOnce("猜拳·布/指手部贴图");
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmRockTexture", MethodType.Getter)]
internal static class OrcaArmRockPatch
{
    private static bool Prefix(CharacterModel __instance, ref Godot.Texture2D __result)
    {
        if (__instance is not Orca) return true;
        __result = ModelDb.Character<Ironclad>().ArmRockTexture;
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmPaperTexture", MethodType.Getter)]
internal static class OrcaArmPaperPatch
{
    private static bool Prefix(CharacterModel __instance, ref Godot.Texture2D __result)
    {
        if (__instance is not Orca) return true;
        __result = ModelDb.Character<Ironclad>().ArmPaperTexture;
        return false;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmScissorsTexture", MethodType.Getter)]
internal static class OrcaArmScissorsPatch
{
    private static bool Prefix(CharacterModel __instance, ref Godot.Texture2D __result)
    {
        if (__instance is not Orca) return true;
        __result = ModelDb.Character<Ironclad>().ArmScissorsTexture;
        return false;
    }
}

// ── ③ 通关成就：枚举里没有 OrcaWin ⇒ 借用 IroncladWin（否则 Enum.Parse 抛异常）──
[HarmonyPatch(typeof(CharacterModel), "RunWonAchievement", MethodType.Getter)]
internal static class OrcaRunWonAchievementPatch
{
    private static bool Prefix(CharacterModel __instance, ref Achievement __result)
    {
        if (__instance is not Orca) return true;

        // 原版实现：Enum.Parse<Achievement>(Id.Entry.Capitalize() + "Win") ⇒ "OrcaWin" 不存在 ⇒ 抛异常
        __result = Achievement.IroncladWin;
        OrcaAssetFallback.LogOnce("通关成就（枚举里没有 OrcaWin）");
        return false;
    }
}