using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace OrcaCharacter;

/// <summary>
///     ★ **商店里的奥卡** —— 把 <see cref="OrcaSkin.MerchantSkeleton" /> 真正接到商店角色上。
///
///     <para><b>这是一个"阶段没接上"的历史遗留</b>（用户 2026-10-04 口述的迭代史）：
///     <list type="number">
///       <item>先把奥卡战士皮肤移植过来、单独成立为一个角色 ⇒ 那时本质是"换了皮的铁甲战士"，
///         <c>scenes/merchant/characters/orca_merchant.tscn</c> 就是**这一阶段**留下的 ——
///         它的 <c>ext_resource</c> 指向**原版** <c>animations/merchant/ironclad/ironclad_merchant_skel_data.tres</c>；</item>
///       <item>后来单独做了能量动画系统；</item>
///       <item>再做皮肤切换系统时，才加出 <see cref="OrcaSkin.MerchantSkeleton" />（板甲/婚纱两条分支）——
///         <b>但从来没有人把它接到场景上</b> ⇒ 于是它成了死代码；</item>
///       <item>之后开始补卡牌。</item>
///     </list>
///     ⇒ 结果：商店里站的一直是**原版铁甲战士**（用户实测反馈：「商店变成了战士哥，而不是奥卡」）。</para>
///
///     <para><b>为什么用运行时改骨架、而不是改场景</b>：场景是静态资源，
///     写死一套骨架就只能显示那一套皮肤；而皮肤是可以切换的。
///     ⇒ 挂在商店角色就绪时按当前皮肤套一次，才是与阶段 3 设计一致的做法。
///     （外部模组 CharacterSkinManager 也是这么做的 —— 它 patch 的是 <c>NMerchantRoom.AfterRoomIsLoaded</c>。）</para>
///
///     <para><b>安全闸</b>（照抄 <see cref="OrcaCharacterIcon" /> 里已验证过的那套）：
///     <c>SpineSprite</c> 是 GDExtension 原生类，C# 侧没有对应类型可 <c>is</c> 判断
///     ⇒ 用 <c>node.GetClass()</c> 比 <c>MegaSprite.spineClassName</c>；
///     骨架是**异步**加载的 ⇒ 必须先过 <c>IsAnimationStateReady()</c> 才能驱动，否则会 fail-fast 抛错。
///     全程 try/catch 记日志，失败不影响商店功能。</para>
/// </summary>
[HarmonyPatch(typeof(NMerchantCharacter), "_Ready")]
internal static class OrcaMerchantSkinPatch
{
    /// <summary>SpineSprite 子节点的名字（我们的场景里就叫这个；原版场景也是）。</summary>
    private const string SpineChildName = "SpineSprite";

    /// <summary>
    ///     商店待机动画的**属性名**。场景里就是这么写的
    ///     （<c>scenes/merchant/characters/orca_merchant.tscn</c> 里 <c>preview_animation = "relaxed_loop"</c>）。
    /// </summary>
    private const string IdleAnimationProperty = "preview_animation";

    /// <summary>
    ///     商店待机动画名。**与皮肤定义同源**：<c>skins/orca/&lt;皮肤&gt;/skin.json</c> 里的
    ///     <c>merchant.animation</c> 就是 <c>relaxed_loop</c>。
    /// </summary>
    private const string IdleAnimation = "relaxed_loop";

    private static void Postfix(NMerchantCharacter __instance)
    {
        try
        {
            Apply(__instance);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 商店外观套皮肤失败（不影响商店功能）：{ex.Message}", 2);
        }
    }

    private static void Apply(Node merchant)
    {
        var path = OrcaSkin.MerchantSkeleton;
        var res = ResourceLoader.Load<Resource>(path);
        if (res == null)
        {
            OrcaLog.Warn($"[Orca] 商店骨架加载失败：{path}", 2);
            return;
        }

        var applied = 0;
        foreach (var sprite in FindSpineSprites(merchant))
        {
            var mega = new MegaSprite(sprite);

            // ⚠️ 骨架异步加载 ⇒ 未就绪时驱动会 fail-fast；此时安静跳过（下一次进商店再套）
            if (!mega.IsAnimationStateReady())
            {
                OrcaLog.Info("[Orca] 商店骨架：SpineSprite 尚未就绪，本次跳过", 2);
                continue;
            }

            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(res));

            // ★ 2026-10-04 补：换骨架后必须**重新指定动画**，否则新骨架不会自动播
            //   ⇒ 人物站住不动（用户实测反馈：「商店人物为静态」）。
            //   场景原本是靠 `preview_animation = "relaxed_loop"` 播的，换骨架把这一步覆盖掉了。
            sprite.Set(IdleAnimationProperty, IdleAnimation);
            applied++;
        }

        if (applied > 0)
        {
            OrcaLog.Info($"[Orca] 商店外观已套皮肤 → {OrcaSkin.Active}（{applied} 个 SpineSprite）", 2);
        }
    }

    /// <summary>在商店角色这棵子树里找所有 SpineSprite（含自己）。</summary>
    private static System.Collections.Generic.IEnumerable<Node> FindSpineSprites(Node root)
    {
        if (root.GetClass() == MegaSprite.spineClassName) yield return root;

        foreach (var child in root.GetChildren())
        {
            if (child.GetClass() == MegaSprite.spineClassName) yield return child;
        }
    }
}
