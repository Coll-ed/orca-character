using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace OrcaCharacter;

/// <summary>
///     ★★ **把奥卡的外观接到"非战斗场景"的角色上** —— 商店 与 篝火。
///
///     <para><b>这是一个"阶段没接上"的历史遗留</b>（用户 2026-10-04 口述的迭代史）：
///     <list type="number">
///       <item><b>阶段 1</b>：先把奥卡战士皮肤移植过来、单独成立为一个角色 ⇒ 那时本质是"换了皮的铁甲战士"，
///         <c>scenes/merchant/characters/orca_merchant.tscn</c> 与
///         <c>scenes/rest_site/characters/orca_rest_site.tscn</c> 都是**这一阶段**留下的 ——
///         它们的 <c>ext_resource</c> 指向**原版**骨架
///         （<c>animations/merchant/ironclad/…</c> / <c>animations/rest_site/ironclad/…</c>）；</item>
///       <item><b>阶段 2</b>：单独做能量动画系统；</item>
///       <item><b>阶段 3</b>：做皮肤切换系统时才加出
///         <see cref="OrcaSkin.MerchantSkeleton" /> / <see cref="OrcaSkin.RestSkeleton" />（板甲/婚纱两条分支）——
///         <b>但从来没有人把它们接到场景上</b> ⇒ 两个访问器都成了**死代码**；</item>
///       <item><b>阶段 4</b>：开始补卡牌。</item>
///     </list>
///     ⇒ 结果：商店与篝火里站的一直是**原版铁甲战士**（用户实测：「商店变成了战士哥，而不是奥卡」）。</para>
///
///     <para><b>为什么在运行时换、而不是改场景</b>：场景是静态资源，写死一套骨架就只能显示那一套皮肤；
///     而皮肤可切换。挂在场景角色就绪时按当前皮肤套一次，才与阶段 3 的设计一致。
///     （外部模组 CharacterSkinManager 也是这么做的 —— 它 patch 的是
///     <c>NMerchantRoom.AfterRoomIsLoaded</c> / <c>NRestSiteCharacter._Ready</c>。）</para>
///
///     <para>★ <b>关键细节：换骨架必须把当前动画取回并设回去</b>。原因两处不同、但都要处理：
///     商店的场景把 <c>preview_animation</c> 写成 <c>"relaxed_loop"</c>，换骨架会把它丢掉
///     ⇒ 人物站住不动（用户实测：「商店人物为静态」）；
///     篝火的场景写的是 <c>"-- Empty --"</c>，动画由代码**按章节**设置
///     （皮肤定义里 <c>act0/act1/act2</c> = <c>overgrowth_loop</c>/<c>hive_loop</c>/<c>glory_loop</c>）
///     ⇒ 换骨架同样会丢掉。
///     ⇒ 所以这里**不硬编码任何动画名**，而是"读出来 → 换骨架 → 写回去"，两个场景同一套逻辑。</para>
///
///     <para><b>安全闸</b>（照抄 <see cref="OrcaCharacterIcon" /> 里已验证过的那套）：
///     <c>SpineSprite</c> 是 GDExtension 原生类，C# 侧没有对应类型可 <c>is</c> 判断
///     ⇒ 用 <c>node.GetClass()</c> 比 <c>MegaSprite.spineClassName</c>；
///     骨架**异步**加载 ⇒ 必须先过 <c>IsAnimationStateReady()</c> 才能驱动，否则会 fail-fast 抛错。
///     全程 try/catch 记日志，失败不影响商店/篝火功能。</para>
/// </summary>
internal static class OrcaSceneSkin
{
    /// <summary>
    ///     SpineSprite 上那个"当前动画"的属性名。
    ///     场景文件里就是这么写的（<c>scenes/rest_site/characters/orca_rest_site.tscn</c> 的
    ///     <c>preview_animation = "-- Empty --"</c>）。
    /// </summary>
    private const string AnimationProperty = "preview_animation";

    /// <summary>
    ///     把 <paramref name="skeletonPath" /> 套到这棵子树里的 SpineSprite 上，
    ///     并**保留它原本的动画**。
    /// </summary>
    /// <returns>成功套上的 SpineSprite 个数（0 = 没找到或未就绪）。</returns>
    internal static int Apply(Node root, string skeletonPath, string what)
    {
        var res = ResourceLoader.Load<Resource>(skeletonPath);
        if (res == null)
        {
            OrcaLog.Warn($"[Orca] {what}骨架加载失败：{skeletonPath}", 2);
            return 0;
        }

        var applied = 0;
        foreach (var sprite in FindSpineSprites(root))
        {
            var mega = new MegaSprite(sprite);

            // ⚠️ 骨架异步加载 ⇒ 未就绪时驱动会 fail-fast；此时安静跳过（下次进这个场景再套）
            if (!mega.IsAnimationStateReady())
            {
                OrcaLog.Info($"[Orca] {what}骨架：SpineSprite 尚未就绪，本次跳过", 2);
                continue;
            }

            // ★ 先记住当前动画（可能来自场景，也可能是代码按章节设的），换完骨架再设回去
            var animation = sprite.Get(AnimationProperty);

            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(res));

            if (animation.VariantType == Variant.Type.String && !string.IsNullOrEmpty(animation.AsString()))
            {
                sprite.Set(AnimationProperty, animation);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>在这棵子树里找所有 SpineSprite（含自己）。</summary>
    private static IEnumerable<Node> FindSpineSprites(Node root)
    {
        if (root.GetClass() == MegaSprite.spineClassName) yield return root;

        foreach (var child in root.GetChildren())
        {
            if (child.GetClass() == MegaSprite.spineClassName) yield return child;
        }
    }
}

/// <summary>商店角色就绪时套皮肤（阶段 1 的场景 + 阶段 3 的系统接线，见 <see cref="OrcaSceneSkin" />）。</summary>
[HarmonyPatch(typeof(NMerchantCharacter), "_Ready")]
internal static class OrcaMerchantSkinPatch
{
    private static void Postfix(NMerchantCharacter __instance)
    {
        try
        {
            var n = OrcaSceneSkin.Apply(__instance, OrcaSkin.MerchantSkeleton, "商店外观");
            if (n > 0) OrcaLog.Info($"[Orca] 商店外观已套皮肤 → {OrcaSkin.Active}（{n} 个 SpineSprite）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 商店外观套皮肤失败（不影响商店功能）：{ex.Message}", 2);
        }
    }
}

/// <summary>篝火角色就绪时套皮肤（同上；动画由代码按章节设，所以更要"取回来再设回去"）。</summary>
[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
internal static class OrcaRestSiteSkinPatch
{
    private static void Postfix(NRestSiteCharacter __instance)
    {
        try
        {
            var n = OrcaSceneSkin.Apply(__instance, OrcaSkin.RestSkeleton, "篝火外观");
            if (n > 0) OrcaLog.Info($"[Orca] 篝火外观已套皮肤 → {OrcaSkin.Active}（{n} 个 SpineSprite）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 篝火外观套皮肤失败（不影响篝火功能）：{ex.Message}", 2);
        }
    }
}
