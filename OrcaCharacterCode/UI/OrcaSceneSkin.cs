using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
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
    ///     商店待机动画名。**三处同源，不是我编的**：
    ///     ① 皮肤定义 <c>skins/orca/&lt;皮肤&gt;/skin.json</c> 的 <c>merchant.animation</c>；
    ///     ② 引擎自己的 <c>NMerchantCharacter._Ready</c> 里写死的
    ///        <c>PlayAnimation("relaxed_loop", loop: true)</c>；
    ///     ③ 场景 <c>scenes/merchant/characters/orca_merchant.tscn</c> 的 <c>preview_animation</c>。
    /// </summary>
    internal const string MerchantIdleAnimation = "relaxed_loop";

    /// <summary>
    ///     选人界面大人物模型的动画名。**来源＝皮肤定义** <c>skins/orca/&lt;皮肤&gt;/skin.json</c>
    ///     的 <c>charselect.animation</c>（两套皮肤都是 <c>"animation"</c>）——
    ///     外部模组 CharacterSkinManager 也是拿这个字段驱动
    ///     （反编译实据 <c>TryApplyCharacterSelectPreview</c> → <c>TryApplyLoopToNode(..., skin.CharacterSelect.Animation, ...)</c>）。
    /// </summary>
    internal const string CharSelectIdleAnimation = "animation";

    /// <summary>
    ///     把 <paramref name="skeletonPath" /> 套到这棵子树里的 SpineSprite 上，
    ///     并在换完之后**用运行时的动画 API 重新驱动动画**。
    /// </summary>
    /// <param name="animation">
    ///     换完骨架后要播的动画名。**必须给**，否则换骨架会把运行时动画状态清掉 ⇒ 人物静止
    ///     （用户实测：「商店人物为静态」）。名字来自皮肤定义（与引擎自己写死的那个值一致）。
    /// </param>
    internal static int Apply(Node root, string skeletonPath, string animation, string what)
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

            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(res));

            // ★★ 2026-10-04 修：换完骨架**必须用运行时的动画状态 API 重新驱动**。
            //    两个坑都踩过：
            //      ① 设 `preview_animation` 没用 —— 那是**编辑器预览属性**，运行时读的是动画状态；
            //      ② 原版 `_Ready` 里是 `RunWhenSpineReady(... => PlayAnimation(...))`，
            //         spine 已就绪时那个回调**当场就执行**，动画在 `_Ready` 内已播上；
            //         我的 Postfix 随后换骨架，把它清掉了 ⇒ 静止。
            //    做法照抄引擎自己的 `NMerchantCharacter.PlayAnimation`：SetAnimation(anim, loop)。
            mega.GetAnimationState().SetAnimation(animation, loop: true);

            applied++;
        }

        return applied;
    }

    /// <summary>
    ///     ★ 选人界面里那块**大人物模型**的宿主节点名。
    ///
    ///     <para>实据（游戏自己的 dll，<c>NCharacterSelectScreen</c>）：
    ///     <c>_bgContainer = GetNode&lt;Control&gt;("AnimatedBg");</c> —— 即 <c>_bgContainer</c>
    ///     就是场景里那个叫 <c>AnimatedBg</c> 的节点。这里**直接用公开的节点名**取，
    ///     不去反射它的私有字段（少一处版本耦合）。</para>
    /// </summary>
    private const string SelectScreenModelHost = "AnimatedBg";

    /// <summary>
    ///     把选人界面那块**大人物模型**换成当前皮肤。
    ///
    ///     <para>★ 拼图：模型挂在 <c>AnimatedBg</c> 下，而 <c>SelectCharacter</c> 会**清空它的全部子节点
    ///     再放一个新模型**（游戏 dll 实据：<c>foreach (child in _bgContainer.GetChildren()) RemoveChildSafely(child);</c>
    ///     之后 <c>AddChildSafely(control)</c>）⇒ 模型就是它的**最后一个子节点**（也是唯一一个）。
    ///     外部管理器同样按"最后一个子节点"取（反编译实据 <c>TryApplyCharacterSelectPreview</c>）。</para>
    ///
    ///     <para>⚠️ 这条链路原先**根本没人接** —— <see cref="OrcaSkin.CharSelectSkeleton" /> 一直是死代码，
    ///     所以用户实测「切换为板甲后，立绘变，但是**人物不变**」：小图与立绘由
    ///     <see cref="OrcaCharacterIcon.RefreshLiveIcons" /> 就地换掉，大模型没人管。</para>
    /// </summary>
    internal static void ApplyToCharacterSelect(NCharacterSelectScreen screen)
    {
        try
        {
            var host = screen.GetNodeOrNull<Control>(SelectScreenModelHost);
            if (host == null)
            {
                OrcaLog.Warn($"[Orca] 选人外观：找不到模型宿主节点 {SelectScreenModelHost}", 2);
                return;
            }

            var model = host.GetChildren().LastOrDefault();
            if (model == null)
            {
                OrcaLog.Warn("[Orca] 选人外观：模型宿主下还没有子节点，本次跳过", 2);
                return;
            }

            var n = Apply(model, OrcaSkin.CharSelectSkeleton, CharSelectIdleAnimation, "选人外观");
            if (n > 0)
                OrcaLog.Info($"[Orca] 选人外观已套皮肤 → {OrcaSkin.Active}（{n} 个 SpineSprite）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 选人外观套皮肤失败（不影响选人）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     在这棵子树里找所有 SpineSprite（含自己）。
    ///
    ///     <para>★ 2026-10-05 改为**递归**：原实现只看自己 + 直接子节点，对商店/篝火那种
    ///     "角色节点直接挂 SpineSprite"的场景够用；但选人界面的大人物模型层级更深
    ///     （模型挂在 <c>_bgContainer</c> 下、中间还有包装节点）⇒ 浅查找会**一个都找不到**。
    ///     方法名与注释本来就写的是"这棵子树"，递归才是它本来的语义。</para>
    /// </summary>
    private static IEnumerable<Node> FindSpineSprites(Node root)
    {
        if (root.GetClass() == MegaSprite.spineClassName) yield return root;

        foreach (var child in root.GetChildren())
        {
            foreach (var found in FindSpineSprites(child)) yield return found;
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
            var n = OrcaSceneSkin.Apply(__instance, OrcaSkin.MerchantSkeleton,
                                        OrcaSceneSkin.MerchantIdleAnimation, "商店外观");
            if (n > 0) OrcaLog.Info($"[Orca] 商店外观已套皮肤 → {OrcaSkin.Active}（{n} 个 SpineSprite）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 商店外观套皮肤失败（不影响商店功能）：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     篝火角色就绪时套皮肤。
///
///     <para>⚠️ <b>2026-10-04：暂时【不换骨架】</b>。原因：篝火的动画是**按章节**的
///     （皮肤定义 <c>rest.act0/act1/act2</c> = <c>overgrowth_loop</c>/<c>hive_loop</c>/<c>glory_loop</c>），
///     而场景里写的是 <c>preview_animation = "-- Empty --"</c> ⇒ 动画由引擎代码按当前章节设。
///     换骨架会把这个动画状态清掉，而"当前是第几章"我在这里拿不到
///     ⇒ 硬换的结果就是**换成了奥卡但静止**（商店刚踩过同一个坑）。</para>
///
///     <para>⇒ 先按"商店验证通过的做法"把商店修对，再回来查引擎 <c>NRestSiteCharacter</c> 是按什么设章节动画的
///     （反编译找它的 <c>PlayAnimation</c> 等价物），然后这里照抄一次。**在此之前保持不动** ——
///     "原版铁甲战士但会动"比"奥卡但静止"更容易发现问题。</para>
/// </summary>
[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
internal static class OrcaRestSiteSkinPatch
{
    private static void Postfix(NRestSiteCharacter __instance)
    {
        try
        {
            // TODO(篝火章节动画): 查清 NRestSiteCharacter 怎么按章节驱动动画后，照抄这里的做法：
            //   OrcaSceneSkin.Apply(__instance, OrcaSkin.RestSkeleton, <当前章节的动画名>, "篝火外观");
            _ = __instance;
            OrcaLog.Info("[Orca] 篝火外观：暂未换骨架（章节动画待接，见 OrcaSceneSkin.cs 注释）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 篝火外观检查出错（不影响篝火功能）：{ex.Message}", 2);
        }
    }
}
