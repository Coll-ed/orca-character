using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace OrcaCharacter;

/// <summary>
///     ★★ **改「欧洛巴斯之触」的遗物文本**（用户 2026-10-05：<i>"文本要改"</i>）。
///
///     <para><b>为什么要改</b>：引擎原文案是
///     <i>"拾起时，将你的初始遗物替换为先古版本。"</i> ——
///     但奥卡**不在** <c>RefinementUpgrades</c> 那张表里（表里只有 5 个原版角色的起始遗物）
///     ⇒ 真按那句执行只会拿到兜底的 <c>Circlet</c>（头环）。
///     我们已经把行为改成「不给头环，改给先古牌【栖途】」（见
///     <c>OrcaPack2Acquisition</c> 的两个补丁）⇒ 文案必须跟着改，否则**界面在骗玩家**。</para>
///
///     <para><b>文本从哪来</b>：直接复用事件选项已经在用的那个键
///     （<c>ORCA_HOMESTEAD.event.description</c> = 「获得【栖途】（先古牌）。」）——
///     <b>单一来源</b>：改一处，事件页那一格与遗物浮窗同时变，不可能对不上。</para>
///
///     <para><b>为什么要拦两个 getter</b>：反编译 <c>RelicModel</c> 可见两个候选
///     （<c>DynamicDescription</c> 与 <c>DynamicEventDescription</c>），
///     而这句"拾起时…"到底走哪个我**没有实机确认**
///     ⇒ 两个都拦，无论哪条渲染都不会漏（拦不到的另一个原样放行，无副作用）。</para>
///
///     <para>⚠️ 边界显式：只对 <see cref="TouchOfOrobas" /> 生效（它是 <c>sealed</c>，判等精确）；
///     非它一律放行；任何异常也放行原逻辑 —— 绝不因为改文案把遗物浮窗弄坏。</para>
/// </summary>
[HarmonyPatch(typeof(RelicModel), "get_DynamicDescription")]
internal static class OrcaOrobasRelicTextPatch
{
    /// <summary>复用事件选项那条文案的本地化键（单一来源，见类摘要）。</summary>
    internal const string TextKey = "ORCA_HOMESTEAD.event.description";

    /// <summary>本地化文件名（本模组的 <c>cards.json</c>）。</summary>
    internal const string LocFile = "cards";

    private static bool Prefix(RelicModel __instance, ref LocString __result)
        => OrcaOrobasRelicText.TryApply(__instance, ref __result);
}

/// <summary>同上，拦另一个候选 getter（<c>DynamicEventDescription</c>）。</summary>
[HarmonyPatch(typeof(RelicModel), "get_DynamicEventDescription")]
internal static class OrcaOrobasRelicEventTextPatch
{
    private static bool Prefix(RelicModel __instance, ref LocString __result)
        => OrcaOrobasRelicText.TryApply(__instance, ref __result);
}

/// <summary>两个补丁共用的判定与取文案（避免复制粘贴出两份规则）。</summary>
internal static class OrcaOrobasRelicText
{
    /// <summary>
    ///     是「欧洛巴斯之触」就把描述换成我们的文案并**跳过原 getter**（返回 false）；
    ///     否则返回 true 放行原逻辑。
    /// </summary>
    internal static bool TryApply(RelicModel instance, ref LocString result)
    {
        try
        {
            if (instance is not TouchOfOrobas) return true;

            // ★★ 角色门禁（用户口径 2026-10-05：「只有角色是奥卡时才启用」）——
            //    别的角色确实有先古版起始遗物可换，那句「替换为先古版本」对他们是**正确**的，
            //    绝不能被我们改成「获得栖途」。
            if (!IsOrcaOwner(instance)) return true;

            result = new LocString(OrcaOrobasRelicTextPatch.LocFile,
                                   OrcaOrobasRelicTextPatch.TextKey);
            return false;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 欧洛巴斯之触·改文案出错（用原文案）：{ex.Message}", 2);
            return true;
        }
    }

    /// <summary>
    ///     持有者是不是奥卡。
    ///     <c>RelicModel.Owner</c> 的可访问性在各模型上不一致（有的是 protected）⇒ 走反射；
    ///     再反射读 <c>Player.Character</c>，避免为了一个判等多引一个命名空间。
    ///     拿不到就返回 <c>false</c>（＝**不改文案**，保持原版），宁可不改也不误改。
    /// </summary>
    private static bool IsOrcaOwner(RelicModel relic)
    {
        try
        {
            var ownerProp = AccessTools.Property(relic.GetType(), "Owner")
                            ?? AccessTools.Property(typeof(RelicModel), "Owner");
            var owner = ownerProp?.GetValue(relic);
            if (owner == null) return false;

            var character = AccessTools.Property(owner.GetType(), "Character")?.GetValue(owner);
            return character is Orca;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     ★★ **压掉「欧洛巴斯之触」自带的"替换前后对照"浮窗**（用户 2026-10-05：
    ///     <i>"指针悬停欧洛巴斯之触弹3框"</i> ⇒ <i>"只留它自己一框"</i>）。
    ///
    ///     <para><b>那两框从哪来</b>（反编译 <c>TouchOfOrobas</c> 实据）：
    ///     <code>
    ///     private List&lt;IHoverTip&gt; _extraHoverTips = new List&lt;IHoverTip&gt;();
    ///     _extraHoverTips.AddRange(relicModel.HoverTips);          // L71 / L95
    ///     protected override IEnumerable&lt;IHoverTip&gt; ExtraHoverTips =&gt; _extraHoverTips;   // L101
    ///     </code>
    ///     它本意是"给你看：起始遗物 → 换成什么"（对奥卡＝银龙血统 → 头环）。
    ///     但**我们已经改了行为**（不给头环、改给栖途，见 <c>OrcaPack2Acquisition</c>）
    ///     ⇒ 那两框**已经不成立、是在误导玩家** ⇒ 压掉，只留它自己那一框。</para>
    ///
    ///     <para>⚠️ 角色门禁照旧（复用外层 <see cref="IsOrcaOwner" />，不复制第二份判定）：
    ///     别的角色的起始遗物**确实**有先古版可换，那句对照对他们是正确的 ⇒ 一律放行。
    ///     拿不到 Owner / 出错也放行 —— 绝不因为我们的改动把浮窗弄坏。</para>
    /// </summary>
    [HarmonyPatch(typeof(TouchOfOrobas), "get_ExtraHoverTips")]
    internal static class HoverTipsPatch
    {
        private static bool Prefix(TouchOfOrobas __instance,
                                   ref System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result)
        {
            try
            {
                if (!IsOrcaOwner(__instance)) return true;      // 不是奥卡 ⇒ 原样（对照对他们是对的）
                __result = System.Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>();
                return false;
            }
            catch
            {
                return true;                                    // 出错放行原逻辑，不弄坏浮窗
            }
        }
    }
}
