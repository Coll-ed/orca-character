using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     日志：奥卡的**每一张牌打出去**都记一行（含自动打出），另外每张牌的效果各记一行。
///
///     挂点＝<c>CardModel.OnPlayWrapper</c>（public，所有出牌的唯一漏斗，自动打出也走它），
///     只对 <see cref="OrcaCard" /> 生效 —— 不打日志给别的角色。
///     上一轮"开局无法抽牌"就是靠日志定位到原版 NParticlesContainer 空引用的，
///     所以这层日志按用户要求做全，后面出任何问题都能直接看日志定位。
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class OrcaCardPlayLogPatch
{
    private static void Prefix(CardModel __instance, Creature? target, bool isAutoPlay)
    {
        if (__instance is not OrcaCard) return;

        try
        {
            var energy = __instance.Owner?.PlayerCombatState?.Energy;
            OrcaLog.Info($"[Orca] 打出 {__instance.Id.Entry}（{(isAutoPlay ? "自动" : "手动")}）" +
                     $"→ 目标={target?.Name ?? "无"}，当前能量={energy?.ToString() ?? "?"}", 2);

            // 打出的牌决定能量球中心用哪个头像（龙头 / 魔剑 / 魔典）
            OrcaOrbIcon.Apply(__instance);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 出牌日志出错：{ex.Message}", 2);
        }
    }
}