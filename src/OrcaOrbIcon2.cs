using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace OrcaCharacter;

/// <summary>
///     能量球中心图标的**三形态**切换（用户口径，2026-09-15 凌晨最终确认）：
///
///       "中间的图标会根据玩家打出书页，而自动去更换龙头 / 魔剑 / 魔典头像，头像与龙头风格一致。
///        这里用 IF elif 进行分割。记得板甲的血黑，婚纱的血白两个形态。"
///       "打击防御不变的这种。"
///       "魔剑与魔典是独立的！三个形态变化！"
///
///     ⇒ **三种形态各自独立，全都画在同一个中心图层上**（`Layers/LayerDragon`）：
///         · 龙头（默认 / 遗物触发时切回）
///         · 魔剑（打嗜血龙剑时切到；画得更大 → 视觉上"穿出"外环，因为中心图层本来就排在外环之后）
///         · 魔典（打龙族魔典时切到）
///       早先那版把魔剑做成"盖在龙头上的独立覆盖层"是错的（那会让两个形态同时出现），已删除。
///
///     · 打击 / 防御等基础牌：**不动**，保持上一次的形态；
///     · 三张图都是**线稿**（用户手绘 → DoG 边缘提取），所以能吃"亮度重映射"材质：
///       板甲映成血黑、婚纱映成血白，不用做两套配色。
/// </summary>
internal static class OrcaOrbIcon
{
    // 龙头形态用场景里原本那张贴图（不写死路径，免得和场景不同步）；只有剑/典有独立的双形态图。
    private const string SwordPathPlate = "res://images/ui/combat/energy_counters/orca/orca_icon_sword.png";
    private const string CodexPathPlate = "res://images/ui/combat/energy_counters/orca/orca_icon_codex.png";
    private const string SwordPathWedding = "res://images/ui/combat/energy_counters/orca/orca_icon_sword_wedding.png";
    private const string CodexPathWedding = "res://images/ui/combat/energy_counters/orca/orca_icon_codex_wedding.png";

    /// <summary>魔剑形态的放大倍数（让它"穿出"外环纹理）。</summary>
    private const float SwordScale = 1.35f;

    /// <summary>高亮覆盖层的淡出时长（秒）。</summary>
    private const float FlashDuration = 0.30f;

    /// <summary>高亮覆盖层序号（唯一命名，便于统一清理）。</summary>
    private static int _flashSeq;

    /// <summary>战斗里那颗能量球（从 RefreshLabel 那条必经之路记下来）。</summary>
    private static NEnergyCounter? _counter;

    /// <summary>龙头（默认）形态的贴图 —— 第一次看到时从场景里记下来，不写死路径，免得和场景不同步。</summary>
    private static Texture2D? _dragonDefault;

    /// <summary>当前形态名（日志用；初始就是龙头）。</summary>
    private static string _currentTag = "龙头";

    /// <summary>
    ///     上次"由卡牌驱动"的形态切换时间戳（毫秒）。
    ///     ★ 用户实测"魔剑无法切换"的根因：嗜血龙剑**自残 → 遗物触发 → 遗物要求切回龙头**，
    ///     同一张牌的同一帧里把刚切好的魔剑顶掉了（日志：切魔剑 → 立刻切回龙头）。
    ///     所以遗物那条路径要避让：卡牌切换后 0.9 秒内，遗物不再抢形态。
    /// </summary>
    private static long _lastSwapMs;

    /// <summary>
    ///     中心图层当前显示的是**自带配色**的图标（剑/典：颜色烤进图里 = 深黑/纯白 + 血色发光描边），
    ///     所以能量球刷新那趟递归换色要跳过它，否则会被"亮度重映射"刷成单色。
    ///     龙头形态是灰色线稿、**需要**被重映射上色 ⇒ 切回龙头时复位成 false。
    /// </summary>
    internal static bool UserArtActive { get; private set; }

    /// <summary>
    ///     记下战斗里那颗能量球。
    ///     ★ 只在"没记过 / 记的那颗已经不在场景里"时才覆盖 —— 否则多人局或换场时，
    ///     后来者的 RefreshLabel 会把引用抢走，"回合补满能量"那条触发就会挂到别人的球上。
    /// </summary>
    internal static void Remember(NEnergyCounter counter)
    {
        if (_counter != null && GodotObject.IsInstanceValid(_counter) && !_counter.IsQueuedForDeletion()) return;
        _counter = counter;
    }

    /// <summary>
    ///     战斗里那颗能量球（只读出口）。给"回合补满能量"那条触发用 ——
    ///     <c>PlayerCombatState.ResetEnergy()</c> 是纯数据层，拿不到节点，只能靠这里记下来的引用。
    /// </summary>
    internal static NEnergyCounter? CurrentCounter => _counter;

    /// <summary>打出一张牌之后调用（只对奥卡的牌有意义）。</summary>
    internal static void Apply(CardModel card)
    {
        try
        {
            if (_counter == null || !GodotObject.IsInstanceValid(_counter)) return;

            // 只处理奥卡自己的卡（OrbForm 定义在 OrcaCard 上）
            if (card is not OrcaCard orca) return;

            var layers = _counter.GetNodeOrNull<Control>("%Layers");
            if (layers == null) return;
            var center = layers.GetNodeOrNull<TextureRect>("LayerDragon");
            if (center == null) return;
            _dragonDefault ??= center.Texture;

            // ★ 用**卡牌自己声明的形态标签**分流（不再写死 is OrcaBloodSword）：
            //   以后新增卡牌只要 `public override OrcaOrbForm OrbForm => OrcaOrbForm.XXX;`，
            //   就能复用同一套"切形态 + 回弹 + 残影 + 外圈白化"动画，这里一行都不用改。
            switch (orca.OrbForm)
            {
                case OrcaOrbForm.Sword:
                    Swap(layers, center, OrcaSkin.BySkin(SwordPathPlate, SwordPathWedding), "魔剑", SwordScale, card);
                    break;

                case OrcaOrbForm.Codex:
                    Swap(layers, center, OrcaSkin.BySkin(CodexPathPlate, CodexPathWedding), "魔典", 1.0f, card);
                    break;

                // ★ 银龙体系（铸血 / 涅槃之握 / 践踏）：主动切回**龙头**（默认那条龙）
                case OrcaOrbForm.Dragon:
                    ApplyDragon(center, $"银龙体系 / {card.Id.Entry}");
                    break;

                default:
                    // None（打击 / 防御 等基础牌）：保持上一次的形态
                    OrcaLog.Info($"[Orca] {card.Id.Entry}：能量球形态保持「{_currentTag}」不变（OrbForm=None）", 2);
                    break;
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 切换能量球形态出错：{ex.Message}", 2);
        }
    }

    /// <summary>遗物触发时切回龙头形态（用户要求："遗物触发时变成龙的图标"）。</summary>
    internal static void ShowDragon()
    {
        try
        {
            // ★ 避让卡牌驱动的形态切换：魔剑/魔典刚切完 0.9 秒内，遗物不抢（否则自残类卡牌会被立刻顶回龙头）
            if (System.Environment.TickCount64 - _lastSwapMs < 900)
            {
                OrcaLog.Info("[Orca] 遗物要求切回龙头，但刚刚由卡牌切过形态 —— 本帧避让（保留卡牌形态）", 2);
                return;
            }
            if (_counter == null || !GodotObject.IsInstanceValid(_counter)) return;
            var center = _counter.GetNodeOrNull<Control>("%Layers")?.GetNodeOrNull<TextureRect>("LayerDragon");
            if (center == null) return;
            _dragonDefault ??= center.Texture;

            ApplyDragon(center, $"遗物触发 / {OrcaSkin.Active}");
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 切回龙头形态出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     把能量球切回**龙头**（默认那条龙）。遗物触发与**银龙体系卡牌**共用这一处。
    /// </summary>
    private static void ApplyDragon(TextureRect center, string why)
    {
        center.Texture = _dragonDefault;
        center.Scale = Vector2.One;
        // 龙头是灰色线稿 → 把皮肤配色材质挂回去（剑/典形态时它是 null）
        var mat = ResourceLoader.Load<Material>(OrcaSkin.EnergyOrbMaterialPath);
        if (mat != null) center.Material = mat;
        UserArtActive = false;
        Pop(center);
        _currentTag = "龙头";
        OrcaLog.Info($"[Orca] 能量球形态 → 龙头（{why}）", 2);
    }

    private static void Swap(Control layers, TextureRect center, string path, string tag, float scale, CardModel card)
    {
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null)
        {
            OrcaLog.Warn($"[Orca] 形态贴图缺失：{path}（本次不改形态）", 2);
            return;
        }

        bool same = center.Texture == tex && Mathf.IsEqualApprox(center.Scale.X, scale);
        center.Texture = tex;
        center.Material = null;          // ★ 颜色烤在图里（深黑/纯白 + 血色发光描边），不套重映射
        UserArtActive = true;

        // 以中心为轴缩放（魔剑要"穿出"外环；龙头/魔典是 1.0）
        var half = center.Size / 2f;
        if (half.X <= 0.5f || half.Y <= 0.5f) half = new Vector2(64f, 64f);
        center.PivotOffset = half;
        center.Scale = new Vector2(scale, scale);

        // ★★ 用户口径（2026-09-15 严肃版）：
        //   "残影回弹：**切换形态时**触发"；
        //   "正常回弹：**不触发残影**，图片会回（弹）一下（如魔典状态打出魔典卡牌）"
        if (same)
        {
            Pop(center);                        // 同形态：只回弹一下，**不放残影、不放高亮**
            _currentTag = tag;
            OrcaLog.Info($"[Orca] 能量球形态已是「{tag}」（{card.Id.Entry}）—— 同形态，只回弹，无残影", 2);
            return;
        }

        Pop(center);                                                        // 回弹
        OrcaEnergyBurst.SpawnGhost(layers, center, 0.06f, $"切到{tag}");     // 残影（浮现 → 扩散淡出）
        SpawnFlash(layers, center, tag);                                    // 高亮覆盖层（"变亮叠加在原本的上面"）

        _lastSwapMs = System.Environment.TickCount64;                        // 记时间，供遗物路径避让
        _currentTag = tag;
        OrcaLog.Info($"[Orca] 能量球形态切换 → {tag}（打出 {card.Id.Entry}，缩放 {scale:F2}×，{OrcaSkin.Active}）—— 回弹 + 残影 + 高亮叠加", 2);
    }

    /// <summary>
    ///     形态切换时的**高亮覆盖层**：复制一份当前图标（就是刚换上的新形态），
    ///     用 <c>Modulate &gt; 1</c> **提亮**后叠在原图标之上，短促淡出 ——
    ///     用户口径："刚切换形态后，[要]变亮叠加在原本的上面"（原来只有硬换贴图，所以看着是"啪"地一下变了）。
    /// </summary>
    private static void SpawnFlash(Control layers, Control center, string tag)
    {
        var flash = center.Duplicate() as Control;
        if (flash == null) return;

        flash.Name = $"OrcaBurstFlash{++_flashSeq}";
        flash.Modulate = new Color(1.55f, 1.55f, 1.55f, 0.85f);   // >1 = 提亮
        layers.AddChildSafely(flash);

        var t = flash.CreateTween();
        t.SetParallel(true);
        t.TweenProperty(flash, "modulate:a", 0.0f, FlashDuration)
         .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(flash, "scale", flash.Scale * 1.18f, FlashDuration)
         .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // 兜底销毁（同残影：不依赖 tween.Finished）
        var timer = flash.GetTree()?.CreateTimer(FlashDuration + 0.2f);
        if (timer != null)
            timer.Timeout += () => { if (GodotObject.IsInstanceValid(flash)) flash.QueueFreeSafely(); };

        OrcaLog.Info($"[Orca] 形态高亮叠加（{tag}）：提亮 1.55× 叠在原图标上，{FlashDuration:F2}s 淡出", 2);
    }

    /// <summary>
    ///     形态切换的过渡：★ 用户反馈"切换的时候会出现残影跳动" —— 元凶是原来用的
    ///     `Back` 缓动 + `From(0.75×)`（会过冲，看着像跳）。现在改成**不跳的淡入 + 轻微收拢**。
    /// </summary>
    private static void Pop(Control node)
    {
        var half = node.Size / 2f;
        if (half.X <= 0.5f || half.Y <= 0.5f) half = new Vector2(64f, 64f);
        node.PivotOffset = half;

        var baseScale = node.Scale;
        var t = node.CreateTween();
        t.SetParallel(true);
        t.TweenProperty(node, "scale", baseScale, 0.30f)
         .From(baseScale * 1.12f)
         .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.TweenProperty(node, "modulate:a", 1.0f, 0.28f)
         .From(0.35f)
         .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }
}