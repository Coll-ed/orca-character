using Godot;
using HarmonyLib;
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

            int nSmall = 0, nIllust = 0;
            Walk(tree.Root, small, illust, ref nSmall, ref nIllust);

            if (nSmall + nIllust > 0)
                OrcaLog.Info($"[Orca] 角色图已就地刷新 → {OrcaSkin.Active}（小图 {nSmall} 个 / 立绘 {nIllust} 个）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 就地刷新角色图出错：{ex.Message}", 2);
        }
    }

    private static void Walk(Node node, Texture2D? small, Texture2D? illust, ref int nSmall, ref int nIllust)
    {
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
                // ★ 2026-10-04 移除「婚纱铺满」特例（用户口径：原因是图不够大，现在补了 1468×2167 的原图）：
                //   原来婚纱用 KeepAspectCovered（铺满、裁掉超出部分），板甲用原版的 KeepAspectCentered，
                //   只为掩盖"婚纱立绘只有 608×768、居中等比会显示成一小块"这件事。
                //   现在婚纱换成 1468×2167（面积 6.8 倍）⇒ 两套皮肤都走**原版居中等比**，不再有特例。
                //
                //   ⚠️ 这里仍然**独立校正**填充方式（不放进"需要换图"的分支里）—— 那是另一个已修 bug 的教训：
                //   用户实测"第一次启动时婚纱覆盖没生效"，根因就是首次启动时贴图**已经是**目标图 ⇒
                //   `Texture != illust` 为假 ⇒ 整段跳过 ⇒ 填充方式从没被设过。所以校正要与换图解耦。
                if (tr.StretchMode != TextureRect.StretchModeEnum.KeepAspectCentered)
                {
                    tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                    nIllust++;
                }

                if (tr.Texture != illust)
                {
                    tr.Texture = illust;
                    nIllust++;
                }
            }
        }

        foreach (var child in node.GetChildren())
            Walk(child, small, illust, ref nSmall, ref nIllust);
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