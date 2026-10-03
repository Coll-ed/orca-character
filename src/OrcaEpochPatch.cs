using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

/// <summary>
///     ★★★ 让奥卡**真正参与**原版的 epoch（时间线）检查 —— 而不是把原版机制跳过。
///
///     <para>
///     原版 <c>ProgressSaveManager</c> 的两个检查是**按类型写死的 switch**（反编译实据）：
///     <code>
///     if (!(character is Ironclad)) … if (!(character is Deprived))
///         throw new ArgumentOutOfRangeException("character", character, null);
///     epochModel = character switch { … Ironclad =&gt; Get&lt;Ironclad5Epoch&gt;(), … };
///     if (epochModel == null) return;          // Deprived 走这条 = "该角色没有 epoch"
///     … 统计本角色胜场 … if (num &gt;= 15) TryObtainEpochMidRun(epochModel, player);
///     </code>
///     </para>
///
///     <para>
///     ⚠️ 上一版的做法是 <c>Prefix</c> 直接 <c>return false</c> **把整个检查跳过** ——
///     那等于"奥卡的这套进度机制是哑的"（用户点破：<i>"尽可能不要去跳过拦截原本机制，而是去跟着原版走"</i>）。
///     现在改成：**给奥卡补上原版没写的那条分支** ——
///     epoch 用 <see cref="OrcaEpochs" /> 里注册好的 <c>Orca5Epoch</c> / <c>Orca6Epoch</c>，
///     统计口径**逐字照抄原版**，最后照原版调 <c>TryObtainEpochMidRun</c>。
///     </para>
///
///     <para>
///     只对 <c>Orca</c> 生效（其它角色 <c>return true</c> 走原版），且**不改动原版任何条目**。
///     </para>
/// </summary>
internal static class OrcaEpochCheck
{
    /// <summary>精英遭遇集合 —— 直接调原版的 private <c>GetEliteEncounters()</c>（口径与它完全一致）。</summary>
    internal static void RunElites(ProgressSaveManager mgr, Player player)
    {
        try
        {
            var method = AccessTools.Method(typeof(ProgressSaveManager), "GetEliteEncounters");
            if (method?.Invoke(null, null) is not HashSet<ModelId> elites)
            {
                OrcaLog.Warn("[Orca] epoch 检查（精英）：拿不到原版 GetEliteEncounters，本次跳过", 2);
                return;
            }
            Run(mgr, player, typeof(Orca5Epoch), elites, 15, "精英");
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] epoch 检查（精英）出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     Boss 遭遇集合 —— 原版<b>没有</b>独立的取法，它是内联算的
    ///     （<c>ModelDb.Acts.SelectMany(a =&gt; a.AllBossEncounters).Select(e =&gt; e.Id)</c>）⇒ 这里照抄。
    /// </summary>
    internal static void RunBosses(ProgressSaveManager mgr, Player player)
    {
        try
        {
            var bosses = ModelDb.Acts
                .SelectMany(act => act.AllBossEncounters)
                .Select(e => e.Id)
                .ToHashSet();
            Run(mgr, player, typeof(Orca6Epoch), bosses, 15, "Boss");
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] epoch 检查（Boss）出错：{ex.Message}", 2);
        }
    }

    /// <summary>统计 + 授予，口径与原版逐字一致，只把 epoch 换成奥卡自己的。</summary>
    private static void Run(ProgressSaveManager mgr, Player player, Type epochType,
                            HashSet<ModelId> encounters, int need, string what)
    {
        var character = player.Character;
        var epoch = EpochModel.Get(EpochModel.GetId(epochType));      // ★ 已由 OrcaEpochRegistry 注册进原版表
        if (epoch == null) return;

        // ── 以下统计逻辑照抄原版 CheckFifteenElites/BossesDefeatedEpoch ──
        int num = 0;
        foreach (var kv in mgr.Progress.EncounterStats)
        {
            if (!encounters.Contains(kv.Value.Id)) continue;
            foreach (var fightStat in kv.Value.FightStats)
            {
                if (fightStat.Character == character.Id)
                {
                    num += fightStat.Wins;
                    break;
                }
            }
        }

        OrcaLog.Info($"[Orca] {what}已击败：{num}/{need}（epoch {epoch.Id}）", 2);
        if (num < need) return;

        var obtain = AccessTools.Method(typeof(ProgressSaveManager), "TryObtainEpochMidRun");
        if (obtain == null)
        {
            OrcaLog.Warn("[Orca] epoch 检查：找不到原版 TryObtainEpochMidRun，无法授予", 2);
            return;
        }
        obtain.Invoke(mgr, new object[] { epoch, player });
    }
}

/// <summary>原版「击败 15 精英 ⇒ Character5Epoch」——奥卡走自己的 <c>Orca5Epoch</c>。</summary>
[HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenElitesDefeatedEpoch")]
internal static class OrcaEliteEpochPatch
{
    private static bool Prefix(ProgressSaveManager __instance, Player localPlayer)
    {
        if (localPlayer?.Character is not Orca) return true;      // 原版角色照旧
        OrcaEpochCheck.RunElites(__instance, localPlayer);
        return false;                                            // 只为奥卡接管（原版对奥卡本来就会抛）
    }
}

/// <summary>原版「击败 15 Boss ⇒ Character6Epoch」——奥卡走自己的 <c>Orca6Epoch</c>。</summary>
[HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
internal static class OrcaBossEpochPatch
{
    private static bool Prefix(ProgressSaveManager __instance, Player localPlayer)
    {
        if (localPlayer?.Character is not Orca) return true;
        OrcaEpochCheck.RunBosses(__instance, localPlayer);
        return false;
    }
}