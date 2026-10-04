using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     角色**小图**随皮肤切换（用户要求："加入在皮肤切换后，主界面下方小图也跟着切换"）。
///
///     API 依据：<c>CharacterModel</c> 上有
///       · <c>IconTexturePath</c> / <c>IconTexture</c>       —— 角色小图（顶栏、主界面等 UI 都用它）
///       · <c>IconOutlineTexturePath</c> / <c>IconOutlineTexture</c> —— 它的描边版
///       · <c>CharacterSelectIconPath</c> / <c>CharacterSelectIcon</c> —— 选人界面那张独立立绘
///     我们只改**角色小图**：婚纱 → 用从"独立立绘"裁出来的头像（88×88，与板甲同尺寸）；
///     板甲 → 保持原样。
///
///     美术来源（用户确认"是单独立绘"）：皮肤包里那张 826KB 的独立立绘（白发 + 黑角 + 白围巾），
///     裁头部 → 88×88 → `character_icon_orca_wedding.png`。
///     ★ 生成它的脚本：见本轮对话里的 python 片段（bust → crop head → 88×88 → ctex/.import）。
/// </summary>
internal static class OrcaCharacterIcon
{
    private const string WeddingIconPath = "res://images/ui/top_panel/character_icon_orca_wedding.png";

    /// <summary>板甲（默认）角色小图 —— UI 里原本那张。</summary>
    private const string PlateIconPath = "res://images/ui/top_panel/character_icon_orca.png";

    /// <summary>
    ///     ★★ 选人界面底部那排小图用的是 <c>CharacterSelectIcon</c>（反编译 <c>NCharacterSelectButton.Init</c> 实锤：
    ///     <c>_icon.Texture = character.CharacterSelectIcon;</c>），**不是** <c>IconTexture</c>。
    ///     这也正对用户说的"是单独立绘" —— 它就是那张独立立绘（板甲用 826KB 那张，婚纱用用户给的新图）。
    /// </summary>
    private const string WeddingSelectIllustPath = "res://images/packed/character_select/char_select_orca_wedding.png";

    private const string PlateSelectIllustPath = "res://images/packed/character_select/char_select_orca.png";

    private static bool _warned;

    /// <summary>当前加载的婚纱立绘（用于判断"这个节点显示的是不是婚纱图"，决定要不要铺满）。</summary>
    private static Texture2D? _weddingIllust;

    private static string TargetPath => OrcaSkin.BySkin(PlateIconPath, WeddingIconPath);

    private static string TargetSelectIllust =>
        OrcaSkin.IsWedding && ResourceLoader.Exists(WeddingSelectIllustPath)
            ? WeddingSelectIllustPath
            : PlateSelectIllustPath;

    /// <summary>
    ///     ★★ 就地刷新：把**当前已经在屏上**的角色图（小图 + 选人界面底部那排的独立立绘）换成目标皮肤那张。
    ///
    ///     起因（实机验证）：两个模型级补丁都生效了（日志 `Harmony 补丁: 15 成功`、资源也在包里），
    ///     但 UI 是**建屏时读一次**的 ⇒ 在皮肤面板里切换时不会重新读属性，看着"没有变"。
    ///     这里在检测到皮肤变化时遍历场景树直接换贴图 —— 不依赖第三方代码，也不重建 UI；
    ///     整棵树只在**变化那一刻**走一遍（不是每帧）。
    /// </summary>
    internal static void RefreshLiveIcons()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;

            var small = ResourceLoader.Load<Texture2D>(TargetPath);
            var illust = ResourceLoader.Load<Texture2D>(TargetSelectIllust);
            _weddingIllust = ResourceLoader.Load<Texture2D>(WeddingSelectIllustPath);   // 供"是否铺满"判断

            // ★ 2026-10-04 新增（用户实测：「切换为板甲后，立绘变，但是人物不变」）：
            //   原来只换 2D 贴图，站在选人界面上的 **Spine 骨架**不动
            //   （Orca.GenerateAnimator 只在"生成动画器"时套骨架 ⇒ 进战斗才生效）。
            //   这里把选人界面的骨架也一起就地重套 ⇒ 与 `OrcaSkin.CharSelectSkeleton` 同一来源。
            var skeletonRes = ResourceLoader.Load<Resource>(OrcaSkin.CharSelectSkeleton);

            int nSmall = 0, nIllust = 0, nSpine = 0;
            Walk(tree.Root, small, illust, skeletonRes, ref nSmall, ref nIllust, ref nSpine);

            if (nSmall + nIllust > 0)
                OrcaLog.Info($"[Orca] 角色图已就地刷新 → {OrcaSkin.Active}（小图 {nSmall} 个 / 立绘 {nIllust} 个）", 2);
            if (nSpine > 0)
                OrcaLog.Info($"[Orca] 选人骨架已就地重套 → {OrcaSkin.Active}（{nSpine} 个 SpineSprite）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 就地刷新角色图出错：{ex.Message}", 2);
        }
    }

    private static void Walk(Node node, Texture2D? small, Texture2D? illust, Resource? skeletonRes,
                             ref int nSmall, ref int nIllust, ref int nSpine)
    {
        // ★ SpineSprite 是 GDExtension 的原生类，C# 侧没有对应类型可 `is` 判断
        //   ⇒ 用 Godot 的原生类名比较（MegaSprite.spineClassName 的实据值就是 "SpineSprite"）。
        if (skeletonRes != null && node.GetClass() == MegaSprite.spineClassName)
        {
            try
            {
                var sprite = new MegaSprite(node);

                // ⚠️ 反编译实据：骨架是**异步**加载的，_Ready 是自下而上跑的
                //   ⇒ 子节点的 _Ready 可能早于父 SpineSprite 就绪；此时驱动会 fail-fast 抛错。
                //   所以必须先过 IsAnimationStateReady() 这道闸，未就绪就跳过（下一次刷新再来）。
                if (!sprite.IsAnimationStateReady())
                {
                    OrcaLog.Info("[Orca] 选人骨架：SpineSprite 尚未就绪，本次跳过（下次刷新再套）", 2);
                }
                else
                {
                    sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skeletonRes));
                    nSpine++;
                }
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 选人骨架重套失败（不影响其它刷新）：{ex.Message}", 2);
            }
        }

        if (node is TextureRect tr && tr.Texture != null)
        {
            var path = tr.Texture.ResourcePath ?? string.Empty;

            if (small != null && path.Contains("character_icon_orca", StringComparison.Ordinal) && tr.Texture != small)
            {
                tr.Texture = small;
                nSmall++;
            }
            else if (illust != null && path.Contains("char_select_orca", StringComparison.Ordinal))
            {
                // ★ 2026-10-04 修复（用户实测：「切换为婚纱后，立绘会是原格式，然后才放大占满框」）：
                //   原来是**先按旧贴图算填充方式、再换贴图** ⇒ 切换那一帧 tr.Texture 还是板甲那张
                //   ⇒ showingWedding 取到 false ⇒ 先按 KeepAspectCentered（原尺寸）画一帧，
                //   下一次刷新才改成 KeepAspectCovered（铺满）⇒ 肉眼就是"先小后大"。
                //   修法：**先换图，再按"将要显示的图"（illust）算填充方式**。
                //   （原注释里那条"解耦"必须保留：首次启动时贴图可能已经是婚纱、但填充方式还没设过，
                //     所以填充方式仍然独立校正，不放在 `tr.Texture != illust` 分支里。）
                if (tr.Texture != illust)
                {
                    tr.Texture = illust;
                    nIllust++;
                }

                bool showingWedding = _weddingIllust != null && illust == _weddingIllust;
                var wantStretch = showingWedding
                    ? TextureRect.StretchModeEnum.KeepAspectCovered      // 婚纱：铺满（超出部分只裁显示层，不动原图）
                    : TextureRect.StretchModeEnum.KeepAspectCentered;    // 板甲：原版居中等比
                if (tr.StretchMode != wantStretch)
                {
                    tr.StretchMode = wantStretch;
                    nIllust++;
                }
            }
        }

        foreach (var child in node.GetChildren())
            Walk(child, small, illust, skeletonRes, ref nSmall, ref nIllust, ref nSpine);
    }

    /// <summary>
    ///     选人界面**打开时**也刷一次（延迟 0.25s，等那排按钮建好）。
    ///     为什么需要：皮肤文件里早就存着婚纱了 ⇒ "变化"事件不会触发 ⇒ 只在变化时刷新会漏掉这种情况。
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "_Ready")]
    internal static class SelectScreenReadyPatch
    {
        private static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen __instance)
        {
            try
            {
                // ① 打开时先刷一次（等那排按钮建好）
                var once = __instance.GetTree()?.CreateTimer(0.25f);
                if (once != null) once.Timeout += () => RefreshLiveIcons();

                // ② ★ 之后每 1 秒轮询一次：选人界面上**没有任何东西会读我们的皮肤属性**，
                //   所以节流刷新不会自己触发 ⇒ 在面板里切皮肤后，那排小图不会跟着变
                //   （用户实测："覆盖了板甲的" —— 切回板甲后图还是婚纱）。
                //   这个 Timer 挂在界面节点下，界面销毁时自动回收，不会泄漏。
                var poll = new Godot.Timer
                {
                    Name = "OrcaSkinIconPoll",
                    WaitTime = 1.0,
                    Autostart = true,
                    OneShot = false,
                };
                __instance.AddChild(poll);
                poll.Timeout += () =>
                {
                    OrcaSkin.Refresh();              // 立刻重读管理器选择（不走 1.5s 节流）
                    RefreshLiveIcons();
                };
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 选人界面刷新挂钩出错：{ex.Message}", 2);
            }
        }
    }

    /// <summary>需要换成婚纱小图吗（只针对奥卡本人 + 婚纱皮肤）。</summary>
    private static bool WantsWedding(CharacterModel model)
    {
        if (model is not Orca) return false;
        if (!OrcaSkin.IsWedding) return false;
        if (ResourceLoader.Exists(WeddingIconPath)) return true;

        if (!_warned)
        {
            _warned = true;
            OrcaLog.Warn($"[Orca] 婚纱角色小图缺失：{WeddingIconPath}（先沿用板甲那张）", 2);
        }
        return false;
    }

    /// <summary>
    ///     ★★ 选人界面底部那排「小图」的真正入口 —— <c>CharacterSelectIcon</c> / <c>CharacterSelectIconPath</c>。
    ///     婚纱 ⇒ 用用户给的独立立绘（原图 608×768，**不裁剪不缩放**）。
    /// </summary>
    [HarmonyPatch(typeof(CharacterModel), "get_CharacterSelectIconPath")]
    internal static class SelectIconPathPatch
    {
        private static void Postfix(CharacterModel __instance, ref string __result)
        {
            try
            {
                if (__instance is not Orca) return;
                if (!OrcaSkin.IsWedding) return;
                if (ResourceLoader.Exists(WeddingSelectIllustPath)) __result = WeddingSelectIllustPath;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 选人立绘路径切换出错：{ex.Message}", 2);
            }
        }
    }

    /// <summary>贴图入口（UI 可能直接取 <c>CharacterSelectIcon</c>，一并覆盖）。</summary>
    [HarmonyPatch(typeof(CharacterModel), "get_CharacterSelectIcon")]
    internal static class SelectIconTexturePatch
    {
        private static void Postfix(CharacterModel __instance, ref CompressedTexture2D __result)
        {
            try
            {
                if (__instance is not Orca) return;
                if (!OrcaSkin.IsWedding) return;

                var tex = ResourceLoader.Load<CompressedTexture2D>(WeddingSelectIllustPath);
                if (tex != null) __result = tex;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 选人立绘贴图切换出错：{ex.Message}", 2);
            }
        }
    }

    /// <summary>路径入口 —— 覆盖所有"按路径取角色小图"的 UI。</summary>
    [HarmonyPatch(typeof(CharacterModel), "get_IconTexturePath")]
    internal static class PathPatch
    {
        private static void Postfix(CharacterModel __instance, ref string __result)
        {
            try
            {
                if (WantsWedding(__instance)) __result = WeddingIconPath;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 角色小图路径切换出错：{ex.Message}", 2);
            }
        }
    }

    /// <summary>贴图入口 —— 有的 UI 直接取 <c>IconTexture</c>（可能已缓存），这里一并覆盖。</summary>
    [HarmonyPatch(typeof(CharacterModel), "get_IconTexture")]
    internal static class TexturePatch
    {
        private static void Postfix(CharacterModel __instance, ref Texture2D __result)
        {
            try
            {
                if (!WantsWedding(__instance)) return;
                var tex = ResourceLoader.Load<Texture2D>(WeddingIconPath);
                if (tex != null) __result = tex;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 角色小图贴图切换出错：{ex.Message}", 2);
            }
        }
    }
}