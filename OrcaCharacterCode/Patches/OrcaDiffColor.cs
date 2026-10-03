using HarmonyLib;
using MegaCrit.Sts2.Core.TextEffects;

namespace OrcaCharacter;

/// <summary>
///     ★★ **让卡面数值变色跟着皮肤走**（用户口径 2026-09-23：
///     「变色能不能按照两套皮肤来改？板甲是浅灰，婚纱是白」）。
///
///     <para><b>实据</b>：卡面里 <c>{X:diff()}</c> 的变色最终由
///     <c>StsTextUtilities.HighlightChangeText(string text, int baseComparison)</c> 产出，
///     反编译后它**写死了两个 BBCode 标签名**：
///     <code>
///     string text2 = ((baseComparison &gt; 0) ? "green" : "red");
///     stringBuilder.Insert(0, "[" + text2 + "]");
///     stringBuilder.Append("[/" + text2 + "]");
///     </code>
///     ⇒ 想换色，只能在这里拦（Prefix 直接给出结果，不再走原版）。
///     用 <c>[color=#RRGGBB]</c> 而不是命名标签 —— 游戏自己的 strings 里就有
///     <c>[color=#00ff00]</c> / <c>[color=#606060]</c> 这类写法，证明这条 BBCode 可用。</para>
///
///     <para><b>两套皮肤</b>（与 <see cref="OrcaSkin.IsWedding" />、
///     <c>Orca.EnergyLabelOutlineColor</c>、<c>Orca.SpeechBubbleColor</c> 同一套口径）：
///     <list type="bullet">
///       <item>板甲 ⇒ <b>浅灰</b> <c>#C8C8C8</c></item>
///       <item>婚纱 ⇒ <b>白</b> <c>#FFFFFF</c></item>
///     </list>
///     用户没有要求区分"升/降"，所以两档同色（原版的绿/红不再使用）。</para>
///
///     <para>⚠️ <c>baseComparison == 0</c>（数值没有变化）时**返回 true 放行**，
///     让原版照旧输出纯文本 —— 这一点必须保留，否则卡面上每个数字都会被套上颜色。</para>
/// </summary>
[HarmonyPatch(typeof(StsTextUtilities), "HighlightChangeText")]
internal static class OrcaDiffColorPatch
{
    /// <summary>板甲的数值高亮色（浅灰）。</summary>
    private const string PlateHex = "C8C8C8";

    /// <summary>婚纱的数值高亮色（白）。</summary>
    private const string WeddingHex = "FFFFFF";

    private static bool Prefix(string text, int baseComparison, ref string __result)
    {
        try
        {
            if (baseComparison == 0) return true;             // 没变化 ⇒ 原版纯文本，别染色

            string hex = OrcaSkin.BySkin(PlateHex, WeddingHex);
            __result = $"[color=#{hex}]{text}[/color]";
            return false;
        }
        catch
        {
            return true;                                      // 出错就退回原版绿/红，绝不弄坏卡面
        }
    }
}