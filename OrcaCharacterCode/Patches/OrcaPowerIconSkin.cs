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

    /// <summary>
    ///     ★ 2026-10-07 新增：「睥睨」(<see cref="OrcaOverlookPower" />) 的 Id（小写形态）。
    ///     小图标已按 Id 约定补好了 <c>orca_overlook_power[_wedding].tres</c>（图集精灵），
    ///     但**大图标**是按 Id 找 <c>images/powers/&lt;id&gt;.png</c> —— 那个文件本工程做不出来
    ///     （新 PNG 必须经 Godot 编辑器重导成 <c>.ctex</c>，本机没有编辑器）⇒ 见文件末尾的改道 patch。
    /// </summary>
    internal const string OrcaOverlookPowerId = "orca_overlook_power";

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

/// <summary>
///     ★★ <b>2026-10-07 新增：睥睨（<see cref="OrcaOverlookPower" />）的大图标改道</b>。
///
///     <para><b>为什么缺</b>（反编译实据 <c>PowerModel.cs:104-138</c>）：
///     <list type="bullet">
///       <item>小图标 = <c>PackedIconPath</c>，按 Id 找 <c>atlases/power_atlas.sprites/&lt;id&gt;.tres</c>
///         —— 本轮已补好 <c>orca_overlook_power[_wedding].tres</c> ✓；</item>
///       <item>大图标 = <c>BigIconPath</c>（<b>private</b>），按 Id 找 <c>images/powers/&lt;id&gt;.png</c>；
///         找不到再找 <c>powers/beta/&lt;id&gt;.png</c>；都找不到则退回引擎自带的
///         <c>powers/missing_power.png</c>（通用占位图）。</item>
///     </list>
///     而 <b>本工程做不出新的 PNG</b>：包里的纹理只认已导入的 <c>.ctex</c>，
///     新 PNG 必须经 Godot 编辑器重导（本机没有编辑器）⇒ 让睥睨改道去用**已经导好**的
///     杀意图标 <c>orca_killing_intent_power.png</c>：语义正好一致 ——
///     当初那套三件套（杀意/智慧/血统）**就是睥睨的旧实现**，效果同为「使打出的牌额外打出一次」。</para>
///
///     <para>★ <b>为什么这条 patch 是安全的</b>：<c>ResolvedBigIconPath</c> 是
///     <b>public</b> 取值器（<c>PowerModel.cs:118</c>，与 private 的 <c>BigIconPath</c> 不同）
///     ⇒ 挂 Prefix 不依赖私有签名；且任何异常都 <c>return true</c> 放行原逻辑、
///     只记一条 Warn（不静默），绝不会把 buff 栏弄坏。</para>
/// </summary>
[HarmonyPatch(typeof(PowerModel), "get_ResolvedBigIconPath")]
internal static class OrcaPowerBigIconPatch
{
    /// <summary>睥睨借用的那张图（已导入，路径与 <c>orca_overlook_power.tres</c> 里引用的同一份）。</summary>
    private static string OverlookBigIconPath =>
        ImageHelper.GetImagePath("powers/orca_killing_intent_power.png");

    private static bool Prefix(PowerModel __instance, ref string __result)
    {
        try
        {
            var id = __instance?.Id.Entry.ToLowerInvariant();
            if (id != OrcaPowerIconSkinPatch.OrcaOverlookPowerId) return true;

            __result = OverlookBigIconPath;
            return false;
        }
        catch (System.Exception ex)
        {
            // 不静默：改道失败只会让大图标退回引擎的通用占位图，但根因要可查
            OrcaLog.Warn($"[Orca] 睥睨大图标改道失败（退回引擎占位图）：{ex.Message}");
            return true;
        }
    }
}