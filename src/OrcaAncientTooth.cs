using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace OrcaCharacter;

/// <summary>
///     ★★ 让原版遗物「**古老牙齿**」（<c>ARCHAIC_TOOTH</c>）把 **嗜血龙剑 → 嗜血魔剑**
///     （用户口径 2026-09-16：<i>"先古牌，获得古老牙齿遗物后升级龙剑为魔剑"</i>）。
///
///     <para>
///     反编译实据（<c>_verify\\decomp-tooth\\...ArchaicTooth.cs</c>）：映射是一张**硬编码的私有静态字典**
///     <code>
///     private static Dictionary&lt;ModelId, CardModel&gt; TranscendenceUpgrades => new()
///     {
///         { ModelDb.Card&lt;Bash&gt;().Id,          ModelDb.Card&lt;Break&gt;() },
///         { ModelDb.Card&lt;Neutralize&gt;().Id,    ModelDb.Card&lt;Suppress&gt;() },
///         { ModelDb.Card&lt;Unleash&gt;().Id,       ModelDb.Card&lt;Protector&gt;() },
///         { ModelDb.Card&lt;FallingStar&gt;().Id,    ModelDb.Card&lt;MeteorShower&gt;() },
///         { ModelDb.Card&lt;Dualcast&gt;().Id,       ModelDb.Card&lt;Quadcast&gt;() }
///     };
///     </code>
///     —— 每个角色各一条（5 个角色），**奥卡是第 6 个 ⇒ 必须自己补一条**，否则拾起古老牙齿时
///     <c>GetTranscendenceStarterCard</c> 找不到我们的起始卡 ⇒ 走 fallback 变成「怀疑」，玩家拿不到魔剑。
///     </para>
///
///     <para>
///     ★ 因为它是**属性**（每次 get 都新建字典），patch getter 的返回值最省事、也不破坏原版条目。
///     </para>
/// </summary>
[HarmonyPatch(typeof(ArchaicTooth), "get_TranscendenceUpgrades")]
internal static class OrcaArchaicToothPatch
{
    private static void Postfix(ref Dictionary<ModelId, CardModel> __result)
    {
        try
        {
            __result ??= new Dictionary<ModelId, CardModel>();
            __result[ModelDb.Card<OrcaBloodSword>().Id] = ModelDb.Card<OrcaBloodBlade>();
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 古老牙齿映射注入失败（龙剑→魔剑将不可用）：{ex.Message}", 2);
        }
    }
}