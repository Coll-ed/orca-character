using System;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **本模组自带的皮肤切换面板**（用户口径 2026-10-05：「下一步就是重新做皮肤切换管理器了」）。
///
///     <para><b>形态（照用户 2026-10-05 的手绘草图）</b>：标题＝**当前皮肤名** → 中间一块**实时小人**
///     （战斗待机动画，随皮肤变）→ 左右两个箭头（《 》）循环切换两套皮肤。</para>
///
///     <para><b>面板交互</b>（用户口径 2026-10-05）：
///     <list type="bullet">
///       <item><b>拖动</b>：按住面板左键拖走，松手时记住位置；</item>
///       <item><b>滚轮缩放</b>：整个面板连续 0.1 步进缩放（<see cref="ZoomMin" />–<see cref="ZoomMax" />），同样记住；</item>
///       <item>位置与倍率都存在本模组的 user 配置目录（<c>panel.json</c>，hardcode-ok：user 是引擎虚拟路径协议名）：
///         <c>{x,y,zoom}</c>；**位置可选** —— 没拖过就不写绝对坐标，保住右下角预设的分辨率自适应。</item>
///     </list></para>
///
///     <para><b>为什么要自己做</b>：切换入口原先完全依赖**外部皮肤包**（<c>奥卡皮肤-Orca\CharacterSkinManager</c>，
///     第三方通用管理器）。用户的设计理念一直是「整合到奥卡角色模组本体」——
///     本模组已经有全部两套骨架（路径见 <see cref="OrcaSkin" /> 的四个访问器），
///     应用侧（战斗 / 商店 / 篝火 / 卡框 / 能量球 / 图标 / 高亮色 / 气泡色）也早已齐全，
///     唯独"点哪儿切"在别人家里 ⇒ 皮肤包没装、或它改了行为，本角色就没法切皮肤。</para>
///
///     <para><b>挂点照外部管理器的实据</b>（反编译 <c>CharacterSkinManager.dll</c>，它在本机实测可用）：
///     <code>
///     [HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]          → 注入面板
///     [HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")] → 通知选中角色 + 刷新面板 + 换选人界面大模型
///     </code></para>
///
///     <para>⚠️ 面板只在**选中奥卡**时显示（别的角色不该看见奥卡的皮肤开关）。</para>
/// </summary>
internal static class OrcaSkinPanel
{
    /// <summary>面板节点名（<c>EnsureInjected</c> 用它判重，避免重复注入）。</summary>
    private const string PanelNodeName = "OrcaSkinPanel";

    /// <summary>面板位置落盘文件名（与本模组皮肤配置同目录，见 <see cref="OrcaSkin.UserStorePath" />）。</summary>
    private const string PanelPosFile = "panel.json";

    // ── 面板几何（具名常量：默认位置来源＝外部管理器的实测可用坐标，见类注释；
    //    尺寸按本面板内容重算：标题 + 预览 + 箭头）──────────────────────────
    private const int DefaultOffsetLeft = -560;
    private const int DefaultOffsetTop = -300;
    private const int DefaultOffsetRight = -340;
    private const int DefaultOffsetBottom = -90;

    /// <summary>预览框尺寸（像素）。</summary>
    private const int PreviewWidth = 120;
    private const int PreviewHeight = 150;

    /// <summary>
    ///     预览小人的缩放。
    ///     <para>来源：<c>scenes/creature_visuals/orca.tscn</c> 里 <c>Visuals</c> 节点的
    ///     <c>scale = 0.28</c>，而该场景的 <c>Bounds</c> 是 242×278 ⇒ 满尺寸约 993 px 高；
    ///     缩到 <see cref="PreviewHeight" /> 附近 ⇒ 约 0.13。</para>
    /// </summary>
    private const float PreviewSpineScale = 0.13f;

    /// <summary>左右箭头按钮的宽度（像素）。</summary>
    private const int ArrowWidth = 34;

    private static PanelContainer? _panel;
    private static Label? _title;
    private static Control? _previewBox;
    private static Node? _previewSpine;
    private static string _previewSkinTag = string.Empty;
    private static bool _previewAnimStarted;

    /// <summary>程序化改状态时的重入闸门（不想把程序性改动当成用户操作）。</summary>
    private static bool _updating;

    /// <summary>拖动中。</summary>
    private static bool _dragging;

    /// <summary>当前面板属于哪个屏幕（点箭头时要回头刷新它）。</summary>
    private static NCharacterSelectScreen? _screen;

    /// <summary>注入面板（幂等：已经注入过就直接返回）。</summary>
    internal static void EnsureInjected(NCharacterSelectScreen screen)
    {
        _screen = screen;

        if (screen.GetNodeOrNull<PanelContainer>(PanelNodeName) != null) return;

        try
        {
            var panel = new PanelContainer { Name = PanelNodeName, Visible = false };
            ApplyDefaultPlacement(panel);

            var column = new VBoxContainer();
            panel.AddChild(column);

            _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            column.AddChild(_title);

            // ── 中间：左箭头 | 实时小人 | 右箭头 ──────────────────────
            var row = new HBoxContainer();
            column.AddChild(row);

            row.AddChild(MakeArrow("《", -1));
            _previewBox = new Control
            {
                CustomMinimumSize = new Vector2(PreviewWidth, PreviewHeight),
                MouseFilter = Control.MouseFilterEnum.Pass,   // 让空白处的点击落到面板上（可拖动）
            };
            row.AddChild(_previewBox);
            row.AddChild(MakeArrow("》", +1));

            // 拖动：挂在面板自己身上（箭头按钮会先吃掉自己的点击）
            panel.GuiInput += @event => OnPanelGuiInput(@event, panel);

            screen.AddChild(panel);
            _panel = panel;

            LoadState(panel);          // 位置（可选）+ 倍率（可选）

            OrcaLog.Info($"[Orca] 皮肤面板已注入选人界面（选中奥卡时显示；"
                       + $"位置={(_positionIsCustom ? "已记住" : "默认")}，倍率 {_zoom:F1}×）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 皮肤面板注入失败（选人界面照常用，只是没有切换入口）：{ex.Message}", 2);
        }
    }

    private static void ApplyDefaultPlacement(Control panel)
    {
        panel.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.KeepSize, 0);
        panel.OffsetLeft = DefaultOffsetLeft;
        panel.OffsetTop = DefaultOffsetTop;
        panel.OffsetRight = DefaultOffsetRight;
        panel.OffsetBottom = DefaultOffsetBottom;
    }

    private static Button MakeArrow(string text, int direction)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(ArrowWidth, PreviewHeight),
        };
        button.Pressed += () => CycleSkin(direction);
        return button;
    }

    /// <summary>按当前选中的角色刷新面板：**只有奥卡**才显示；并同步标题/预览/小人。</summary>
    internal static void Refresh(NCharacterSelectScreen screen, CharacterModel? character)
    {
        var panel = screen.GetNodeOrNull<PanelContainer>(PanelNodeName);
        if (panel == null) return;

        var isOrca = character is Orca;
        panel.Visible = isOrca;
        if (!isOrca) return;

        SyncState();
    }

    /// <summary>
    ///     同步"现在穿的是哪套"：标题 = 当前皮肤名、预览小人换骨架、箭头可见性。
    ///
    ///     <para>给选人界面那个 1 秒轮询也调一次：玩家可能在**外部管理器**的面板里切皮肤，
    ///     那时本面板会滞后 ⇒ 一起同步。顺带承担"小人动画还没起来就再试一次"的补偿
    ///     （骨架异步加载，见 <see cref="ApplyPreviewSkin" /> 的说明）。</para>
    /// </summary>
    internal static void SyncState()
    {
        _updating = true;
        try
        {
            OrcaSkin.Refresh();                       // 立刻重读（不吃 1.5s 节流）
            var wedding = OrcaSkin.IsWedding;

            if (_title != null) _title.Text = wedding ? "婚纱" : "板甲";

            EnsurePreview();
            ApplyPreviewSkin();
            EnsureOnScreen();          // ★ 每轮刷新都兜一次：坏存档/改分辨率/改窗口大小都会被拉回屏内
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 皮肤面板刷新出错：{ex.Message}", 2);
        }
        finally
        {
            _updating = false;
        }
    }

    // ── 预览小人（裸 SpineSprite，不实例化带战斗脚本的场景）─────────────────

    /// <summary>
    ///     建预览小人（只建一次）。
    ///
    ///     <para>★ 为什么不用 <c>scenes/creature_visuals/orca.tscn</c>：那个场景的根节点挂着
    ///     **战斗脚本** <c>src/Core/Nodes/Combat/NCreatureVisuals.cs</c>（场景实据），
    ///     在选人界面里实例化它等于把战斗逻辑搬出战斗环境 —— 风险不值当。
    ///     这里直接造一个裸 <c>SpineSprite</c>（类名实据＝同场景里 <c>type="SpineSprite"</c>），
    ///     只设骨架数据与待机动画。</para>
    /// </summary>
    private static void EnsurePreview()
    {
        if (_previewSpine != null || _previewBox == null) return;

        var created = ClassDB.Instantiate(MegaSprite.spineClassName);
        if (created.AsGodotObject() is not Node2D spine)
        {
            OrcaLog.Warn($"[Orca] 皮肤预览：造不出 {MegaSprite.spineClassName}（面板照常可用，只是没有小人）", 2);
            return;
        }

        // 脚底对齐预览框底部中间（角色原点在脚下）
        spine.Position = new Vector2(PreviewWidth / 2f, PreviewHeight);
        spine.Scale = Vector2.One * PreviewSpineScale;

        _previewBox.AddChild(spine);
        _previewSpine = spine;
        OrcaLog.Info("[Orca] 皮肤预览小人已建立", 2);
    }

    /// <summary>把当前皮肤的**战斗骨架**套到预览小人上，并播待机动画。</summary>
    private static void ApplyPreviewSkin()
    {
        if (_previewSpine == null) return;

        var tag = OrcaSkin.Active + "/" + OrcaSkin.BattleSkeleton;
        if (tag == _previewSkinTag && _previewAnimStarted) return;   // 已经套好且动画在播 ⇒ 不重复动它

        var res = ResourceLoader.Load<Resource>(OrcaSkin.BattleSkeleton);
        if (res == null)
        {
            OrcaLog.Warn($"[Orca] 皮肤预览：骨架加载失败 {OrcaSkin.BattleSkeleton}", 2);
            return;
        }

        var mega = new MegaSprite(_previewSpine);
        if (tag != _previewSkinTag)
        {
            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(res));
            _previewSkinTag = tag;
            _previewAnimStarted = false;                            // 换了骨架，动画要重播
        }

        // ⚠️ 骨架异步加载 ⇒ 未就绪时驱动会 fail-fast。这里**不报错**，留给下一次 SyncState 重试
        //    （选人界面那个 1 秒轮询会一直调过来；一旦就绪就起播）。
        if (!mega.IsAnimationStateReady()) return;

        mega.GetAnimationState().SetAnimation(OrcaBattleIdleAnimation, loop: true);
        _previewAnimStarted = true;
        OrcaLog.Info($"[Orca] 皮肤预览已套 {OrcaSkin.Active}（{OrcaBattleIdleAnimation}）", 2);
    }

    /// <summary>预览播的战斗待机动画名 —— 与皮肤定义 <c>skins/orca/&lt;皮肤&gt;/skin.json</c> 的 <c>battle.idle</c> 同源。</summary>
    private const string OrcaBattleIdleAnimation = "idle_loop";

    // ── 切换 ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     左右箭头：在两套皮肤之间循环。
    ///     <para>只有两套 ⇒ 左右两个方向都是"切到另一套"（不是错误，是两套的必然结果；
    ///     这两个箭头是为了照草图的形态，将来加第三套时改成按 <paramref name="direction" /> 取相邻即可）。</para>
    /// </summary>
    private static void CycleSkin(int direction)
    {
        if (_updating) return;

        try
        {
            var target = OrcaSkin.IsWedding ? OrcaSkin.Plate : OrcaSkin.Wedding;
            OrcaLog.Info($"[Orca] 皮肤箭头（{(direction < 0 ? "左" : "右")}）→ {target}", 2);

            OrcaSkin.SetSkin(target);                  // 双写 + 就地刷新角色图

            var screen = _screen;
            if (screen != null) OrcaSceneSkin.ApplyToCharacterSelect(screen);

            SyncState();
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 切换皮肤出错：{ex.Message}", 2);
        }
    }

    // ── 拖动 + 位置持久化 ──────────────────────────────────────────────────

    private static void OnPanelGuiInput(InputEvent @event, Control panel)
    {
        try
        {
            if (@event is InputEventMouseButton button)
            {
                // ── 滚轮：整个面板连续缩放（用户口径 2026-10-05：「加一个自定义放大」+「鼠标滚轮缩放」+「连续 0.1 步进」）
                if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                {
                    Zoom(+ZoomStep);
                    return;
                }
                if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                {
                    Zoom(-ZoomStep);
                    return;
                }

                if (button.ButtonIndex == MouseButton.Left)
                {
                    if (button.Pressed)
                    {
                        BeginDrag(panel);
                    }
                    else if (_dragging)
                    {
                        _dragging = false;
                        EnsureOnScreen();          // 松手前先夹回屏内，绝不把屏幕外坐标写进存档
                        SaveState();
                    }
                }
            }
            else if (@event is InputEventMouseMotion motion && _dragging)
            {
                // 拖动中也夹：面板到边缘就停住，**根本出不去**（上一版就是在这里跑出屏幕的）
                panel.Position = ClampToViewport(panel, panel.Position + motion.Relative);
            }
        }
        catch (Exception ex)
        {
            _dragging = false;
            OrcaLog.Warn($"[Orca] 皮肤面板拖动/缩放出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     开始拖动：把锚点换成左上（之后 <c>Position</c> 就是自由坐标）。
    ///
    ///     <para>⚠️ <b>2026-10-05 修 user 报的"一拖就消失了"</b>：原先这里写的是
    ///     <c>SetAnchorsPreset(TopLeft, keepOffsets: true)</c> —— 面板本来是**右下角锚点**，
    ///     偏移 <c>(-560, -300)</c> 的含义是"距右边缘 560、距下边缘 300"；
    ///     换锚点时**保留这两个偏移数值**，坐标就成了 <c>(-560, -300)</c>
    ///     ⇒ 整个面板跑到屏幕左上角**外面**去了（算术即可证）。
    ///     现在**显式**读回屏幕位置再写回去，不依赖 <c>keepOffsets</c> 的语义。</para>
    /// </summary>
    private static void BeginDrag(Control panel)
    {
        var keep = panel.GlobalPosition;                 // 先记下屏幕上的真实位置
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.GlobalPosition = keep;                     // 再写回 ⇒ 视觉位置不变，但坐标自由了
        _positionIsCustom = true;
        _dragging = true;
    }

    /// <summary>把记住的位置套上去（锚点同样换成左上 + 显式设坐标）。</summary>
    private static void ApplySavedPlacement(Control panel, Vector2 pos)
    {
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.Position = ClampToViewport(panel, pos);
        _positionIsCustom = true;
    }

    /// <summary>
    ///     把想放的位置**夹进屏幕内**（硬边界，任何来源都要过这道）。
    ///
    ///     <para>★★ 2026-10-05 加（用户实测：「没有显示，是不是上次我拖一下直接消失了，记录了坐标？
    ///     我是不是丢到屏幕外面去了」——**用户判断完全正确**）：
    ///     上一版拖动有 bug（换锚点却保留右下角偏移 ⇒ 坐标变负、面板飞出屏幕），
    ///     而它**松手时把那个屏幕外坐标存进了 <c>panel.json</c>**
    ///     （实测存档值 <c>{"x":-788.75,"y":-488.25}</c>）⇒ 拖动修好之后，
    ///     新代码又把坏坐标**忠实地读了回来**，面板就再也不出现。
    ///     ⇒ 位置必须有硬边界：**存档 / 拖动 / 缩放**落到的坐标一律先夹进视口，
    ///     于是即便历史存档是坏的，也会被自动拉回屏内。</para>
    /// </summary>
    private static Vector2 ClampToViewport(Control panel, Vector2 desired)
    {
        var size = panel.Size;
        if (size.X <= 0f || size.Y <= 0f) return desired;    // 还没排版 ⇒ 算不出来，原样返回（下次再夹）

        var view = panel.GetViewportRect().Size;
        var scaled = size * _zoom;

        // 缩放以面板中心为支点（PivotOffset = Size/2）⇒ 视觉左上角 = Position + (Size − scaled)/2
        var shift = (size - scaled) / 2f;
        var visual = desired + shift;

        var maxX = Mathf.Max(0f, view.X - scaled.X);
        var maxY = Mathf.Max(0f, view.Y - scaled.Y);
        return new Vector2(Mathf.Clamp(visual.X, 0f, maxX), Mathf.Clamp(visual.Y, 0f, maxY)) - shift;
    }

    /// <summary>把面板拉回屏内（当前坐标不合法时才有变化；拖动中不干预，避免和手指打架）。</summary>
    private static void EnsureOnScreen()
    {
        if (_panel == null || _dragging) return;
        _panel.Position = ClampToViewport(_panel, _panel.Position);
    }

    // ── 缩放（整个面板）────────────────────────────────────────────────────

    /// <summary>
    ///     缩放倍率下限。理由：低于它标题与箭头已经难以辨认，且预览小人基本看不清。
    /// </summary>
    private const float ZoomMin = 0.5f;

    /// <summary>
    ///     缩放倍率上限。理由：本面板默认约 220×210，3× 时约 660×630 —— 再大就会盖住
    ///     选人界面的角色与按钮，失去"浮窗"的意义。
    /// </summary>
    private const float ZoomMax = 3.0f;

    /// <summary>滚轮一格的倍率步进（用户口径 2026-10-05：「连续 0.1 步进」）。</summary>
    private const float ZoomStep = 0.1f;

    /// <summary>当前倍率（1.0 = 默认大小）。</summary>
    private static float _zoom = 1.0f;

    /// <summary>位置是否已被用户改过（改过才落盘绝对坐标；没改过就继续用右下角预设，分辨率自适应）。</summary>
    private static bool _positionIsCustom;

    private static void Zoom(float delta)
    {
        var next = Math.Clamp(_zoom + delta, ZoomMin, ZoomMax);
        if (Math.Abs(next - _zoom) < 0.001f) return;     // 到顶/到底了，不重复落盘

        _zoom = next;
        ApplyZoom();
        EnsureOnScreen();                // 放大后可能超出屏幕 ⇒ 立刻夹回来
        SaveState();
        OrcaLog.Info($"[Orca] 皮肤面板缩放 → {_zoom:F1}×（范围 {ZoomMin:F1}–{ZoomMax:F1}）", 2);
    }

    /// <summary>把倍率应用到整个面板：以**面板中心**为缩放中心（否则会往右下角长）。</summary>
    private static void ApplyZoom()
    {
        if (_panel == null) return;
        _panel.PivotOffset = _panel.Size / 2f;
        _panel.Scale = Vector2.One * _zoom;
    }

    /// <summary>
    ///     读回记住的面板状态（<c>panel.json</c>：位置与倍率**各自可选**）。
    ///     缺哪一项就用哪一项的默认（位置＝右下角预设、倍率＝1×）。
    /// </summary>
    private static void LoadState(Control panel)
    {
        try
        {
            var path = OrcaSkin.UserStorePath(PanelPosFile);
            if (path == null || !System.IO.File.Exists(path)) return;

            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("x", out var x) && root.TryGetProperty("y", out var y))
                ApplySavedPlacement(panel, new Vector2((float)x.GetDouble(), (float)y.GetDouble()));

            if (root.TryGetProperty("zoom", out var zoom))
            {
                _zoom = Math.Clamp((float)zoom.GetDouble(), ZoomMin, ZoomMax);
                ApplyZoom();
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 读皮肤面板状态失败（用默认位置与倍率）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     落盘面板状态：**倍率**总是写；**位置**只在用户拖过之后才写绝对坐标
    ///     （没拖过就继续用右下角预设 ⇒ 保住分辨率自适应）。
    /// </summary>
    private static void SaveState()
    {
        try
        {
            var path = OrcaSkin.UserStorePath(PanelPosFile);
            if (path == null || _panel == null) return;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            var payload = _positionIsCustom
                ? JsonSerializer.Serialize(new { x = _panel.Position.X, y = _panel.Position.Y, zoom = _zoom })
                : JsonSerializer.Serialize(new { zoom = _zoom });

            System.IO.File.WriteAllText(path, payload);
            OrcaLog.Info(_positionIsCustom
                ? $"[Orca] 皮肤面板状态已记住：位置 ({_panel.Position.X:F0}, {_panel.Position.Y:F0})、倍率 {_zoom:F1}×"
                : $"[Orca] 皮肤面板倍率已记住：{_zoom:F1}×", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 写皮肤面板状态失败：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     选中角色时刷新皮肤面板 + 把选人界面那块**大人物模型**换成当前皮肤。
///
///     <para>挂点与签名照外部管理器（<c>CharacterSkinCharacterSelectSwitchPatches</c>）的实据：
///     <c>NCharacterSelectScreen.SelectCharacter(NCharacterSelectButton, CharacterModel)</c> 的 postfix，
///     按名字绑定 <c>characterModel</c>。</para>
///
///     <para>⚠️ 大模型是 <c>SelectCharacter</c> **内部**才挂到 <c>_bgContainer</c> 上的，
///     而骨架是异步加载的 ⇒ 这里**立刻试一次 + 下一帧再试一次**（未就绪时 <see cref="OrcaSceneSkin" />
///     会记日志跳过，不会 fail-fast）。</para>
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class OrcaSkinPanelSelectPatch
{
    private static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        try
        {
            OrcaSkinPanel.EnsureInjected(__instance);
            OrcaSkinPanel.Refresh(__instance, characterModel);

            if (characterModel is not Orca) return;

            OrcaSceneSkin.ApplyToCharacterSelect(__instance);
            Callable.From(() => OrcaSceneSkin.ApplyToCharacterSelect(__instance)).CallDeferred();
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 选人界面套皮肤失败（不影响选人）：{ex.Message}", 2);
        }
    }
}
