using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **让 buff 图标跟着皮肤走**（用户 2026-09-23：「严格做婚纱/板甲 —— 深红与红白二中区分」）。
///
///     <para><b>为什么必须 patch 而不能 override</b>（反编译 `PowerModel` 实据）：
///     <code>
///     public string PackedIconPath =&gt; ImageHelper.GetImagePath("atlases/power_atlas.sprites/" + Id.Entry.ToLowerInvariant() + ".tres");
///     public string IconPath =&gt; PackedIconPath;
///     public Texture2D Icon  =&gt; ResourceLoader.Load&lt;Texture2D&gt;(PackedIconPath, null, ResourceLoader.CacheMode.Reuse);
///     </code>
///     <c>PackedIconPath</c> 上**没有 `virtual`** ⇒ 不能像 <c>OrcaBloodline.PackedIconPath</c>
///     （那是 <c>RelicModel</c>，那份是 virtual）那样覆写，只能拦 getter。
///     （<c>BigIconPath</c> 是 private，但它天然按 id 找 <c>images/powers/&lt;id&gt;.png</c> —— 我们已放好，
///     所以 tooltip 大图不需要额外处理。）</para>
///
///     <para><b>作用域收窄</b>（历史教训：原版图集染色会污染别的角色 —— 与"格挡音全局替换"同类）：
///     <list type="number">
///       <item>**板甲直接放行**（原版路径 = 我们放的深红版，零改动）；</item>
///       <item>只有**婚纱**且**确实做了婚纱版**（`&lt;id&gt;_wedding.tres` 存在）时才改道；
///         ⇒ 没做婚纱版的 buff（原版 buff、别人的 buff）**完全不受影响**。</item>
///     </list></para>
///
///     <para>写法与 <see cref="OrcaEnergyIconPatch" /> 同一套范式（prefix + 常量/派生路径 + <see cref="OrcaSkin.BySkin" />）。</para>
/// </summary>
[HarmonyPatch(typeof(PowerModel), "get_PackedIconPath")]
internal static class OrcaPowerIconSkinPatch
{
    /// <summary>婚纱版图标的后缀（板甲版无后缀，直接走原版路径）。</summary>
    private const string WeddingSuffix = "_wedding";

    /// <summary>
    ///     ★ 自写「翱翔」(<see cref="OrcaSoarPower" />) 的 Id（小写形态）。
    ///     它借用**原版翱翔的图标**（用户口径：「图标还是翱翔图标而已」）——
    ///     因为 <c>PackedIconPath</c> 是按 Id 约定找图的，自写类的 Id 找不到任何图，
    ///     所以在这里改道。Id 规则（实测）：类名 <c>FlutterPower</c> → <c>FLUTTER_POWER</c>。
    /// </summary>
    private const string OrcaSoarPowerId = "orca_soar_power";

    /// <summary>原版翱翔的图标路径（游戏本体 pck 内，已实测存在）。</summary>
    private static string OrcaSoarPowerIconPath =>
        ImageHelper.GetImagePath("atlases/power_atlas.sprites/soar_power.tres");

    private static bool Prefix(PowerModel __instance, ref string __result)
    {
        try
        {
            var id = __instance?.Id.Entry.ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return true;

            // ★ 自写翱翔：借原版翱翔的图标（与皮肤无关，先于皮肤判断处理）
            if (id == OrcaSoarPowerId)
            {
                __result = OrcaSoarPowerIconPath;
                return false;
            }

            if (!OrcaSkin.IsWedding) return true;                 // ★ 板甲 = 放行原样

            var wedding = id + WeddingSuffix;
            var path = ImageHelper.GetImagePath("atlases/power_atlas.sprites/" + wedding + ".tres");

            // 没做婚纱版就放行（别让缺图把 buff 图标变成空白）
            if (!ResourceLoaderExists(path)) return true;

            __result = path;
            return false;
        }
        catch
        {
            return true;                                          // 出错就退回原版路径，绝不弄坏 buff 栏
        }
    }

    /// <summary>资源是否存在（用 Godot 的 <c>ResourceLoader.Exists</c>；包一层是为了彻底隔离异常）。</summary>
    private static bool ResourceLoaderExists(string path)
    {
        try
        {
            return Godot.ResourceLoader.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}