using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace OrcaCharacter;

/// <summary>
///     战斗里那颗能量球（连它周围的光/火特效）按皮肤换色：**板甲＝灰黑 / 婚纱＝红白**。
///     素材还是原来那套（战士的红色/橙色能量），只是换成**我们自己的 shader 材质**
///     （<c>res://shaders/orca_orb_tint.gdshader</c>：按亮度在「暗色→亮色」两个色之间重映射）。
///
///     挂点＝<c>NEnergyCounter.RefreshLabel</c> 的 **postfix**：
///     游戏自己会在这个方法里给 5 个图层设材质（没能量时换成 <c>energy_orb_dark</c>）、
///     并把 <c>Layers.Modulate</c> 压成 DarkGray；我们在它之后再覆盖一次材质，
///     于是最终生效的是我们的配色，而它那层"没能量就压暗"的 modulate 依然叠加生效。
/// </summary>
[HarmonyPatch(typeof(NEnergyCounter), "RefreshLabel")]
internal static class OrcaEnergyCounterPatch
{
    private static string _logged = string.Empty;

    /// <summary>
    ///     每个计数器上一次见到的能量值（用来判断"补满"）。
    ///     ⚠️ 值类型必须是**有参数构造器的 class** —— 之前用 `int[]`，
    ///     `ConditionalWeakTable.GetOrCreateValue` 会抛
    ///     "Cannot dynamically create an instance of type 'System.Int32[]'"（日志实锤）。
    /// </summary>
    private sealed class EnergyBox
    {
        public int Value;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NEnergyCounter, EnergyBox> _lastEnergy = new();

    private static void Postfix(NEnergyCounter __instance)
    {
        try
        {
            var t = Traverse.Create(__instance);
            // 记下这颗能量球（打牌时要靠它换中心图标）
            OrcaOrbIcon.Remember(__instance);

            var player = t.Field("_player").GetValue<Player>();
            if (player == null || player.Character is not Orca) return;

            var path = OrcaSkin.EnergyOrbMaterialPath;
            var mat = ResourceLoader.Load<Material>(path);
            if (mat == null)
            {
                OrcaLog.Warn($"[Orca] 能量球换色材质加载失败：{path}", 2);
                return;
            }

            int n = 0;
            n += ApplyTo(t.Field("_layers").GetValue<Control>(), mat);          // 能量球图层（外框 LayerRing 已被上面跳过）
            n += ApplyTo(t.Field("_rotationLayers").GetValue<Control>(), mat);  // 会旋转的两层
            // ★ 外框单独挂"扫光材质"：同一套配色 + 每段依次变白/半透明/回原色的扫光
            n += ApplyRingMaterial(t.Field("_layers").GetValue<Control>());
            // ★ 周围的光/火特效**不**挂这个材质：它们是粒子/翻页火焰，按亮度重映射后会变成
            //   "黑块带一条红线"（用户实测反馈），所以只保留原版特效本身。
            //   要连特效一起换色的话，只能改成 modulate 提色，而不是整套重映射。

            var tag = $"{OrcaSkin.Active}/{n}";
            if (tag != _logged)
            {
                _logged = tag;
                OrcaLog.Info($"[Orca] 能量球换色 → {OrcaSkin.Active}（{n} 个节点挂上 {path}）", 2);
            }

            // ★ 注意：这里**不做**任何"归位/自愈"—— 
            //   能量为 0 时原版会把 _layers.Modulate 压成 DarkGray（那是**原版行为**，不是残影残留），
            //   我一度误判成"残影没清掉"，加的"每次刷新自愈"反而把刚生成的残影当场清掉，
            //   导致"切换形态看不到残影"。现已撤销（用户确认那是能量 0/非 0 的变化）。

            // ★ 若外框特效已过结束时间却仍停在非零状态（tween 被打断的情况），这里复位一次；
            //   只在"没有正在播的特效"时动手，绝不打断动画。这样"外圈永久变白"不可能发生。
            OrcaEnergyBurst.ResetIfIdle("刷新时兜底");

            // ★★ 触发条件（用户 2026-09-16 修订）：
            //     IF   能量从 **0** 获得（花光之后又补上）    ← 只在"0 → 有"这一刻放
            //     ELIF 回合结束补满能量                        ← 见 OrcaEnergyRefillPatch（ResetEnergy）
            //     删掉了旧的「AnimIn 滑入入场」触发：它就是"每回合放两次、互相 Kill"的元凶。
            var box = _lastEnergy.GetOrCreateValue(__instance);
            var energy = player.PlayerCombatState?.Energy ?? 0;
            if (energy == 0) OrcaEnergyBurst.NoteZero();
            else if (box.Value == 0 || OrcaEnergyBurst.JustHitZero())
                OrcaEnergyBurst.Play(__instance, 0f, "0 能量后获得");
            box.Value = energy;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 能量球换色出错：{ex.Message}", 2);
        }
    }

    /// <summary>把外框（LayerRing）换成当前皮肤的"扫光材质"；不扫时就是普通换色，视觉上与能量球一致。</summary>
    private static int ApplyRingMaterial(Control? layers)
    {
        if (layers == null) return 0;
        var ring = layers.GetNodeOrNull<Control>("LayerRing");
        if (ring == null)
        {
            foreach (var child in layers.GetChildren())
                if (child is Control c && c.Name.ToString().Contains("LayerRing", StringComparison.Ordinal)) { ring = c; break; }
        }
        if (ring == null) return 0;

        var path = OrcaSkin.RingSweepMaterialPath;
        if (ring.Material is ShaderMaterial cur &&
            string.Equals(cur.ResourcePath, path, StringComparison.Ordinal)) return 1;   // 已经是目标材质

        var mat = ResourceLoader.Load<Material>(path);
        if (mat == null) { OrcaLog.Warn($"[Orca] 外框扫光材质加载失败：{path}", 2); return 0; }
        ring.Material = mat;
        return 1;
    }

    /// <summary>把材质递归挂到子树里所有 CanvasItem 上（图层 / 精灵 / 粒子都吃这一套）。</summary>
    private static int ApplyTo(Node? root, Material mat)
    {
        if (root == null) return 0;

        // 龙头残影 / 剑与魔典残像 都自带材质、外框自带"扫光"材质，换色这一趟必须绕开它们
        var nodeName = root.Name.ToString();
        if (nodeName.Contains("OrcaBurst", StringComparison.Ordinal) ||
            nodeName.Contains("LayerRing", StringComparison.Ordinal)) return 0;
        // 中心图层若正显示用户手绘图（魔典），它的配色是画在图里的，别被重映射刷成单色
        if (OrcaOrbIcon.UserArtActive && root is TextureRect tr &&
            tr.Name.ToString() == "LayerDragon") return 0;

        int n = 0;
        if (root is CanvasItem ci)
        {
            ci.Material = mat;
            n++;
        }

        foreach (var child in root.GetChildren())
            n += ApplyTo(child, mat);

        return n;
    }
}

/// <summary>
///     ★★ 触发源①：**回合结束补满能量**（用户口径的 ELIF）。
///
///     挂点＝<c>PlayerCombatState.ResetEnergy()</c> 的 **postfix** —— 这是能量"补满"这个动作
///     唯一的入口，decompiled <c>CombatManager.SetupPlayerTurn</c> 实锤：
///     <code>
///     if (Hook.ShouldPlayerResetEnergy(state, player)) { SfxCmd.Play(".../gain_energy"); player.PlayerCombatState.ResetEnergy(); }
///     else { player.PlayerCombatState.AddMaxEnergyToCurrent(); }
///     </code>
///     只要这一句被调用（也就是"补满"真的发生了，每回合都发生，第一回合也是），我们就放动画 ——
///     **不依赖能量当时是不是 0**，所以"满能量结束回合照样补满"也能触发。
///
///     ★ 为什么必须等 <see cref="OrcaEnergyBurst.RefillDelay" /> 秒再放：
///       这一句跑在 <c>Draw</c> / <c>AnimIn</c> **之前**，此刻能量计数器还在屏幕外
///       (-480,128) 的滑动起点上，动画会整段播在屏幕外 ⇒ 玩家什么都看不到
///       （旧版"补满动画不出现"的真凶之一）。等滑入结束再放，正好落在计数器和龙头刚就位的时候。
/// </summary>
[HarmonyPatch(typeof(PlayerCombatState), "ResetEnergy")]
internal static class OrcaEnergyRefillPatch
{
    private static void Postfix(PlayerCombatState __instance)
    {
        OrcaEnergyBurst.NoteRefill();
        var counter = OrcaOrbIcon.CurrentCounter;      // RefreshLabel/OnEnergyChanged 记下来的那颗球
        if (counter == null || !GodotObject.IsInstanceValid(counter)) return;
        OrcaEnergyBurst.Play(counter, OrcaEnergyBurst.RefillDelay, "回合补满能量");
    }
}

/// <summary>
///     ★★ 触发源②：**能量归零后重新获得**（用户口径的 IF）。
///
///     挂点＝<c>NEnergyCounter.OnEnergyChanged(old, new)</c> 的 postfix —— 游戏自己在这个方法里
///     只在"能量变多"时才重启光火特效，我们这里再确认一次 <c>new &gt; old</c>。
///
///     ★ 0 的判定改用**时间窗**（见 <see cref="OrcaEnergyBurst.NoteZero" />）：
///       原来直接读 <c>old == 0</c>，而"刚打完最后一张牌的归零"与"补满"之间隔着一整段出牌/结算动画，
///       <c>old</c> 经常已经不是 0 了 ⇒ 用户报的"被能量归零拦截了"。
///       现在改成"刚刚（200ms 内）才归零过"就算 0 → 有。
/// </summary>
[HarmonyPatch(typeof(NEnergyCounter), "OnEnergyChanged")]
internal static class OrcaEnergyBurstPatch
{
    private static void Postfix(NEnergyCounter __instance, int __0, int __1)
    {
        if (__1 > __0)
        {
            if (__0 == 0 || OrcaEnergyBurst.JustHitZero())
                OrcaEnergyBurst.Play(__instance, 0f, "0 能量后获得");
            return;
        }
        if (__1 == 0) OrcaEnergyBurst.NoteZero();      // 记下"这一刻能量见底"
    }
}

/// <summary>
///     「龙登场」动作本体。用户定的规格（2026-09-14 夜）：
///       ① 从大到小的**弹跳**（慢一点）；
///       ② 弹跳**完成后**，一个半透明的**纯白色（婚纱）/ 血黑色（板甲）龙头**向外扩散一下然后消散；
///       ③ 周围那圈纹理**整转 360° 一次**（双形态都要有 —— 颜色本来就随皮肤材质走，所以两种都自动生效）。
/// </summary>
internal static class OrcaEnergyBurst
{
    /// <summary>
    ///     「回合补满能量」的延后播放时长（秒）。
    ///     原版 <c>NEnergyCounter.AnimIn()</c> 是 0.6 秒的 Expo-Out 滑入，而且 <c>ResetEnergy()</c>
    ///     跑在它**之前**（此时计数器还在屏幕外 (-480,128)）⇒ 不等这 0.62 秒，动画整段播在屏幕外。
    /// </summary>
    internal const float RefillDelay = 0.62f;

    /// <summary>「回合补满能量」这个触发源的标识串（<see cref="Play" /> 里用它区分 IF / ELIF 两条规则）。</summary>
    internal const string TriggerRefill = "回合补满能量";

    /// <summary>外框"白色前缘绕圈扫过一整圈"的时长（秒）。</summary>
    private const float SweepDuration = 0.95f;

    /// <summary>回退第一段：白 → 灰（秒）。用户口径"婚纱下是白色变成灰色再变成白色"。</summary>
    private const float ReturnGray = 0.32f;

    /// <summary>回退第二段：灰 → 原色（秒）。**必须逐渐过渡**（用户点名批评过"突然变回去"）。</summary>
    private const float ReturnColor = 0.38f;

    /// <summary>当前正在播的外框 tween —— 只有"能量归零后重新获得"那一条允许打断它。</summary>
    private static Tween? _sweepTween;

    /// <summary>当前外框材质 —— 兜底复位用（不依赖 tween 是否跑完）。</summary>
    private static ShaderMaterial? _sweepMat;

    /// <summary>本次外框特效预计结束时间（毫秒时间戳）—— 到点后强制复位。</summary>
    private static long _sweepUntilMs;

    /// <summary>上一次"回合补满能量"的时刻 —— 用来给"0 → 有"那条触发去重（同一回合只放一次）。</summary>
    private static long _lastRefillMs;

    /// <summary>上一次"能量见底（0）"的时刻 —— 用户口径里的"0"，用时间窗判定而不是只看 old==0。</summary>
    private static long _lastZeroMs;

    /// <summary>「刚归零」的时间窗（毫秒）：出牌结算的余波超过这个宽度就不算"0 → 有"了。</summary>
    private const long ZeroWindowMs = 200;

    /// <summary>「刚补满」的时间窗（毫秒）：这段时间内不再放"0 → 有"，避免同一回合放两次。</summary>
    private const long RefillWindowMs = 1500;

    /// <summary>整套外框特效总时长 = 扫白 + 白→灰 + 灰→原色。</summary>
    private const float SweepTotal = SweepDuration + ReturnGray + ReturnColor;

    /// <summary>记录「回合补满能量」这一刻（<c>ResetEnergy</c> 触发）。</summary>
    internal static void NoteRefill() => _lastRefillMs = System.Environment.TickCount64;

    /// <summary>记录「能量见底」这一刻。</summary>
    internal static void NoteZero() => _lastZeroMs = System.Environment.TickCount64;

    /// <summary>刚刚才补满过（同一回合的"0 → 有"应当让位）。</summary>
    internal static bool JustRefilled() => System.Environment.TickCount64 - _lastRefillMs < RefillWindowMs;

    /// <summary>刚刚才归零过（等价于用户口径里的 old == 0，但能跨过出牌结算的延迟）。</summary>
    internal static bool JustHitZero()
    {
        long now = System.Environment.TickCount64;
        return _lastZeroMs > 0 && now - _lastZeroMs < ZeroWindowMs;
    }

    /// <summary>
    ///     ★ 硬复位：把外框材质的所有特效参数归零（回到"纯皮肤配色"状态）。
    ///     tween 被 Kill / 被打断时的唯一保险 —— 否则外圈会永久停在白色（用户实测）。
    /// </summary>
    internal static void HardReset(ShaderMaterial? mat, string why)
    {
        if (mat == null || !GodotObject.IsInstanceValid(mat)) return;
        try
        {
            mat.SetShaderParameter("sweep_strength", 0.0f);
            mat.SetShaderParameter("sweep_whiteout", 0.0f);
            mat.SetShaderParameter("sweep_gray", 0.0f);
            mat.SetShaderParameter("sweep_angle", 0.0f);
            OrcaLog.Info($"[Orca] 外框硬复位（{why}）：strength/whiteout/gray/angle 全部归零", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 外框硬复位出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     若特效已过预计结束时间而材质还停在非零特效状态，就复位一次。
    ///     由 RefreshLabel（能量任何变化都会走）调用 —— 只在"没有正在播的特效"时动手，绝不打断动画。
    /// </summary>
    internal static void ResetIfIdle(string why)
    {
        if (_sweepMat == null || !GodotObject.IsInstanceValid(_sweepMat)) return;
        if (System.Environment.TickCount64 < _sweepUntilMs) return;    // 还在播，绝不打扰
        HardReset(_sweepMat, why);
    }
    /// <summary>龙头残影扩散到的倍数 / 时长 / 起始透明度。</summary>
    private const float GhostToScale = 2.40f;

    /// <summary>
    ///     残影时长 —— 用户反馈"残影有的时候叠加在原版的图标上"：
    ///     缩短总时长（0.14 + 0.42 ≈ 0.56s），并配合"每次能量刷新清一遍遗留"，让它不可能久留。
    /// </summary>
    private const float GhostDuration = 0.42f;
    private const float GhostAlpha = 0.55f;

    /// <summary>
    ///     残影的**起始**缩放 —— 用户反馈"残影出现有点突然"：
    ///     旧写法从 1.0× 起，一出来就整块盖在图标上；改成从比图标小（0.72×）开始向外长出去。
    /// </summary>
    private const float GhostFromScale = 0.72f;

    /// <summary>残影"浮现"用时（秒）：透明度 0 → <see cref="GhostAlpha" />，同时微放大。解决"出现有点突然"。</summary>
    private const float GhostFadeIn = 0.14f;

    /// <summary>极短去重窗口（毫秒）：同一次能量变化会被 OnEnergyChanged 与 RefreshLabel 各报一次。</summary>
    private static long _lastPlayMs;
    /// <summary>残影节点序号 —— 每个残影取唯一名，便于"清掉所有遗留残影"时按前缀扫。</summary>
    private static int _ghostSeq;
    internal static void Play(NEnergyCounter? counter, float delay = 0f, string trigger = "?")
    {
        try
        {
            if (counter == null || !GodotObject.IsInstanceValid(counter)) { OrcaLog.Warn("[Orca] 龙出场：计数器无效", 2); return; }
            if (!counter.IsInsideTree()) { OrcaLog.Warn("[Orca] 龙出场：计数器不在场景树里", 2); return; }

            bool isRefill = trigger == TriggerRefill;

            // ★★ 同一回合「补满」与「0 → 有」只能放一次：
            //    补满(ResetEnergy) 之后，能量值一变（比如打出一张 0 费牌再补回来）会再报一次"0 → 有"，
            //    若两条都放，第二条会把第一条 Kill 掉重新开始 —— 玩家看到的就是"卡住/不动"。
            //    所以：补满只在极短窗口内去重；"0 → 有"在补满后 1.5 秒内一律让位。
            long now = System.Environment.TickCount64;
            if (isRefill)
            {
                if (now - _lastPlayMs < 120) { OrcaLog.Info($"[Orca] 外框：{trigger} 与上一次间隔过近（<120ms），跳过", 2); return; }
            }
            else if (JustRefilled())
            {
                OrcaLog.Info($"[Orca] 外框：{trigger} 落在「回合补满能量」之后 —— 本回合已播，跳过", 2);
                return;
            }
            _lastPlayMs = now;

            var layers = counter.GetNodeOrNull<Control>("%Layers") ?? FindByName(counter, "Layers");
            var dragon = layers?.GetNodeOrNull<Control>("LayerDragon") ?? FindByName(counter, "LayerDragon");
            if (dragon == null || layers == null || !dragon.IsInsideTree())
            {
                OrcaLog.Warn($"[Orca] 龙出场：找不到 LayerDragon（layers={(layers != null)}）", 2);
                return;
            }

            var half = dragon.Size / 2f;
            if (half.X <= 0.5f || half.Y <= 0.5f) half = new Vector2(64f, 64f);   // 布局还没算出来时的兜底
            dragon.PivotOffset = half;                                            // 以中心放大

            OrcaLog.Info($"[Orca] 补满（{trigger}）：**只放外圈白化**（{SweepDuration:F2}s / 不旋转）" +
                     $"—— 回弹+残影按用户要求已挪到「切换武器形态」时才触发（见 OrcaOrbIcon.Swap）", 2);

            // ★ 用户口径（最终）：
            //   · 获得能量（每回合补满）＝ **只放外圈白化特效**；
            //   · 回弹 + 残影 ＝ **只在切换武器形态时**触发。
            SweepRing(layers, delay, trigger);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 能量补满的外圈特效出错：{ex.Message}", 2);
        }
    }
    /// <summary>
    ///     龙头残影：复制一份 LayerDragon（布局/贴图/尺寸全都自动一致），
    ///     换成"亮度重映射"材质的**纯色模式**（dark = bright = 目标色 ⇒ 整个龙头变成一块纯色剪影），
    ///     再放大 + 淡出，播完自己 QueueFree。
    ///     · 婚纱 = 纯白 (1,1,1)；板甲 = 血黑 (0.16,0.012,0.02)
    ///     ⚠ 用户实测"残影出现了两次"：旧写法只 Kill 了龙本体的 tween，
    ///       上一次**已经生成的残影节点**还留在场景里继续飘 → 这里每次生成前先把旧的清掉。
    /// </summary>
    /// <summary>
    ///     清掉图层里所有残影节点（名字前缀 <c>OrcaBurstGhost</c>）。
    ///     用户反馈"残影有的时候叠加在原版的图标上" ⇒ 除了 SceneTreeTimer 兜底销毁，
    ///     这里再给一个"随手清一遍"的入口（形态切换、以及**每次能量刷新**都会调）。
    /// </summary>
    internal static void ClearGhosts(Control? layers)
    {
        if (layers == null || !GodotObject.IsInstanceValid(layers)) return;
        int cleaned = 0;
        foreach (var child in layers.GetChildren())
        {
            if (child is Node n && n.Name.ToString().StartsWith("OrcaBurst", StringComparison.Ordinal))
            {
                n.QueueFree();
                cleaned++;
            }
        }
        if (cleaned > 0) OrcaLog.Info($"[Orca] 残影：清掉 {cleaned} 个遗留残影", 2);
    }

    internal static void SpawnGhost(Control layers, Control dragon, float delay, string trigger)
    {
        if (layers == null || !GodotObject.IsInstanceValid(layers)) return;

        ClearGhosts(layers);   // 先把场上遗留残影清干净

        var ghost = dragon.Duplicate() as Control;
        if (ghost == null) { OrcaLog.Warn("[Orca] 残影：复制来源节点失败", 2); return; }

        ghost.Name = $"OrcaBurstGhost{++_ghostSeq}";
        ghost.Material = dragon.Material;        // 跟随来源：线稿形态是 null（颜色已烤进图），龙头形态是皮肤材质
        ghost.Modulate = new Color(1f, 1f, 1f, 0f);   // ★ 从**全透明**起步（旧写法一上来就是 0.55，所以"出现有点突然"）
        ghost.PivotOffset = dragon.PivotOffset;
        ghost.Scale = new Vector2(GhostFromScale, GhostFromScale) * dragon.Scale;
        layers.AddChildSafely(ghost);

        var baseScale = dragon.Scale;
        OrcaLog.Info($"[Orca] 残影（{trigger}）：浮现 {GhostFadeIn:F2}s（0 → {GhostAlpha:F2} 透明）→ " +
                 $"{GhostFromScale:F2}× 长到 {GhostToScale:F2}× 再用 {GhostDuration:F2}s 淡出", 2);

        // ① 浮现：淡入 + 微放大（解决"突然出现"）
        var tIn = ghost.CreateTween();
        tIn.SetParallel(true);
        tIn.TweenProperty(ghost, "modulate:a", GhostAlpha, GhostFadeIn)
           .SetDelay(delay).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tIn.TweenProperty(ghost, "scale", baseScale * 1.06f, GhostFadeIn)
           .From(new Vector2(GhostFromScale, GhostFromScale) * baseScale)
           .SetDelay(delay).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // ② 扩散消散：接在浮现之后（用显式延迟，不依赖 tween 链）
        var tOut = ghost.CreateTween();
        tOut.SetParallel(true);
        tOut.TweenProperty(ghost, "scale", new Vector2(GhostToScale, GhostToScale) * baseScale, GhostDuration)
            .SetDelay(delay + GhostFadeIn).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tOut.TweenProperty(ghost, "modulate:a", 0.0f, GhostDuration)
            .SetDelay(delay + GhostFadeIn).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);

        // ★★ 官方教程（VFX Playback & Caching → PlaySimple）的做法：用 SceneTreeTimer **兜底销毁**。
        //    不能只靠 tween.Finished —— tween 被 Kill 时 finished 不会触发，残影就永久留在场上。
        var tree = ghost.GetTree();
        var timer = tree?.CreateTimer(delay + GhostDuration + 0.3f);
        if (timer != null)
        {
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFreeSafely();
            };
        }
    }

    /// <summary>
    ///     外框扫光（2026-09-16 定稿，用户原话逐条对照）：
    ///       "被绕圈扫过的颜色，逐步渐变为白色，然后所有颜色变为白色时，重新一起变回原来的颜色"
    ///       "起点随机的" + "最后一步全体变色是**逐渐过渡**，而不是突然变回去"
    ///       "婚纱下是**白色变成灰色再变成白色**"
    ///     ⇒ 三段：
    ///       ① 扫白：随机起点，白色前缘绕圈扫过（累积模式，扫过处逐步变白并保持）；
    ///       ② 全白：前缘走满一圈（0 → 360+柔化宽度）；
    ///       ③ 回退（**逐渐**）：白 → 灰（0.32s）→ 原色（0.38s）。
    ///
    ///     ★★ 为什么旧版"起点看起来是固定的"（用户 2026-09-16 反馈）—— **不是随机数的问题**：
    ///        <c>NEnergyCounter.RefreshLabel</c> 在"能量为 0"时会把 <c>_layers</c> 的**每一个子节点**
    ///        （LayerRing 在内）的 <c>Material</c> 换成原版的 <c>energy_orb_dark.tres</c>；等能量回到 >0，
    ///        我们再把它换回"扫光材质"。旧写法在**排程那一刻**就把 mat 取死交给 tween 了，
    ///        所以一旦材质被换掉，tween 改的是一份**已经不在渲染链上**的旧材质：
    ///          · 本次扫光整个丢失（"不能触发"的第二个真凶）；
    ///          · 材质换回来时 <c>sweep_start</c> 是新材质的默认值 0 ⇒ 下次看到的起点永远一样。
    ///        现在改成**取材质延迟到扫描真正开始的那一刻**（闭包里重新取，并同步替换 tween 的目标对象），
    ///        材质怎么换都跟着走；<c>sweep_start</c> 也在那一刻才随机 ⇒ 起点必然随机。
    ///        （shader 侧已数值复算：不同 start ⇒ 变白区段确实跟着转，见 _verify/shader_sweep_math.py）
    /// </summary>
    internal static void SweepRing(Control? layers, float delay, string trigger)
    {
        if (layers == null) return;
        var ring = layers.GetNodeOrNull<Control>("LayerRing") ?? FindByName(layers, "LayerRing");
        if (ring == null) { OrcaLog.Warn("[Orca] 外框：找不到 LayerRing", 2); return; }
        if (ring.Material is not ShaderMaterial current)
        {
            OrcaLog.Warn("[Orca] 外框：LayerRing 上没有扫光材质", 2);
            return;
        }

        // ★ 修复 B-外框扫光（2026-10-03，卡死真因 ✓ 日志栈顶实证）：
        //   本函数用 t.SetParallel(true) 并排多个 TweenProperty ✗ —— Godot 会在 **tween 启动那一刻**
        //   去读属性当前值当**起点**；而 shader_parameter/* 要到 L504 那段 TweenMethod(…SetDelay(t0))
        //   跑起来才被设置 ⇒ 起点读到 Nil ⇒ 与目标 float 类型不匹配 ⇒
        //   validate_type_match(scene/resources/animation.cpp) 断言 ⇒ **卡死** ✓
        //   （实机栈：[2] OrcaEnergyBurst.SweepRing → [1] TweenProperty → [0] godot_icall ✓）
        //   ⇒ 修法：**每个并行的 TweenProperty 都必须显式 .From(起点)** ✓
        //     起点值取自 L504 显式设置的初值（0 / 1 / 上一段终点），不是猜的 ✓
        const float soft = 40f;               // 前缘柔化宽度（度）——越大，"逐步变白"越明显
        float start = (float)(Random.Shared.NextDouble() * 360.0);   // ★ 起点随机（每次都不一样）
        float t0 = delay;
        float t1 = delay + SweepDuration;                 // 全白时刻
        float tMid = t1 + ReturnGray;                     // 灰到最深
        float tEnd = tMid + ReturnColor;                  // 回到原色

        // 开始新特效前先杀掉上一套 tween；上一套改的那份材质顺手复位（它可能已经不在渲染链上了）
        if (_sweepTween != null && _sweepTween.IsValid()) _sweepTween.Kill();
        if (_sweepMat != null && GodotObject.IsInstanceValid(_sweepMat) && _sweepMat != current)
            HardReset(_sweepMat, "材质已被替换");

        // ★ 修复 C-初值提前（2026-10-03，真·根因 ✓）：
        //   Godot 在 TweenProperty **创建那一刻**就会去读属性的当前值，并与目标值做类型校验
        //   （validate_type_match @ scene/resources/animation.cpp）✗ —— 而下面那几个
        //   TweenProperty 动的是 shader_parameter/sweep_*，它们原本要到
        //   TweenMethod(…).SetDelay(t0) 跑起来才被设置 ⇒ 创建时属性还不存在 ⇒ 读到 Nil
        //   ⇒ "Type mismatch between initial and final value: Nil and float" ✓
        //   （.From() 只声明过渡起点，**绕不过这一步** ✗ —— 上一版修复 B 因此无效，已实测确认 ✓）
        //   ⇒ 修法：在 CreateTween 之前先把初值真实写进材质 ✓（值与原 TweenMethod 里的一致 ✓）
        current.SetShaderParameter("sweep_soft", soft);
        current.SetShaderParameter("sweep_start", start);
        current.SetShaderParameter("sweep_accumulate", 1.0f);
        current.SetShaderParameter("sweep_gray", 0.0f);
        current.SetShaderParameter("sweep_angle", 0.0f);
        current.SetShaderParameter("sweep_whiteout", 0.0f);
        current.SetShaderParameter("sweep_strength", 1.0f);

        var t = ring.CreateTween();
        _sweepTween = t;
        _sweepMat = current;
        _sweepUntilMs = System.Environment.TickCount64 + (long)((delay + SweepTotal + 0.15f) * 1000f);
        t.SetParallel(true);

        // ★★ 材质解析延迟到"扫描真正开始的那一刻"：中途被换成 energy_orb_dark 再换回来也照样生效。
        //    用 tween_method 而不是 tween_property —— 因为要换的是**对象**而不只是值。
        t.TweenMethod(Callable.From((float _) =>
        {
            var m = CurrentRingMaterial(layers);
            if (m == null || !GodotObject.IsInstanceValid(m)) return;
            if (m != _sweepMat)
            {
                OrcaLog.Info($"[Orca] 外框：材质在特效开始前被换过（{_sweepMat?.ResourcePath ?? "null"} → {m.ResourcePath}）" +
                         $"—— 本次扫光改挂新材质，起点仍为 {start:F0}°", 2);
                _sweepMat = m;
            }
            m.SetShaderParameter("sweep_soft", soft);
            m.SetShaderParameter("sweep_start", start);
            m.SetShaderParameter("sweep_accumulate", 1.0f);
            m.SetShaderParameter("sweep_gray", 0.0f);
            m.SetShaderParameter("sweep_angle", 0.0f);
            m.SetShaderParameter("sweep_whiteout", 0.0f);
            m.SetShaderParameter("sweep_strength", 1.0f);
        }), 0f, 1f, 0.01f).SetDelay(t0);

        // ① 扫白：白色前缘从随机起点绕圈扫过一整圈（+柔化宽度，保证结尾确实全白）
        t.TweenProperty(current, "shader_parameter/sweep_angle", 360f + soft, SweepDuration)
         .From(0f).SetDelay(t0).SetTrans(Tween.TransitionType.Linear);
        // ③ 回退：白 → 灰
        t.TweenProperty(current, "shader_parameter/sweep_gray", 1.0f, ReturnGray)
         .From(0f)                                   // ★ 修复 B：起点必须显式（L504 设的就是 0）
         .SetDelay(t1).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        // 灰 → 原色（此时把累积量撤掉：灰盖着，看不见切换，随后灰逐渐褪去 ⇒ 视觉上是"逐渐过渡回原色"）
        t.TweenProperty(current, "shader_parameter/sweep_strength", 0.0f, 0.02f).From(1f).SetDelay(tMid);   // ★ 修复 B
        t.TweenProperty(current, "shader_parameter/sweep_gray", 0.0f, ReturnColor)
         .From(1f)                                   // ★ 修复 B：起点 = 上一段终点 1 ✓
         .SetDelay(tMid).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.TweenProperty(current, "shader_parameter/sweep_angle", 0f, 0.01f).From(360f + soft).SetDelay(tEnd);   // ★ 修复 B：起点 = 上一段终点 ✓
        t.Finished += () => OrcaLog.Info($"[Orca] 外框（{trigger}）已逐渐过渡回原配色（白→灰→原色，全程未旋转）", 2);

        // ★★ 硬复位兜底（不计代价保证"不会永久停在白色"）：
        //   实测问题——tween 被 Kill / 没跑到结尾时，材质参数会停在"累积满 + strength=1" ⇒ 外圈**永久变白**。
        //   用 SceneTreeTimer（官方教程 PlaySimple 的做法）在整段动画结束后**强制把参数归零**，
        //   与 tween 是否跑完无关。取的是"当下真正挂在渲染链上"的那份材质。
        var timer = ring.GetTree()?.CreateTimer(delay + SweepTotal + 0.12f);
        if (timer != null)
            timer.Timeout += () =>
            {
                HardReset(CurrentRingMaterial(layers) ?? _sweepMat, trigger);
            };

        OrcaLog.Info($"[Orca] 外框（{trigger}）：白前缘从 {start:F0}° 绕圈扫过 {SweepDuration:F2}s → 全白 → " +
                 $"逐渐回退（白→灰 {ReturnGray:F2}s →原色 {ReturnColor:F2}s，{OrcaSkin.Active}）", 2);
    }

    /// <summary>
    ///     取"此刻真正挂在 LayerRing 上、正在渲染"的扫光材质。
    ///     <c>NEnergyCounter.RefreshLabel</c> 会在能量为 0 时把整个 <c>_layers</c> 换成一整套原版暗色材质，
    ///     所以**任何时候都不能缓存材质引用**，必须现场取。
    /// </summary>
    private static ShaderMaterial? CurrentRingMaterial(Control? layers)
    {
        if (layers == null || !GodotObject.IsInstanceValid(layers)) return null;
        var ring = layers.GetNodeOrNull<Control>("LayerRing") ?? FindByName(layers, "LayerRing");
        return ring?.Material as ShaderMaterial;
    }

    /// <summary>按名字递归找节点（唯一名 `%Xxx` 取不到时的兜底）。</summary>
    private static Control? FindByName(Node root, string name)
    {
        if (root is Control c && c.Name == name) return c;

        foreach (var child in root.GetChildren())
        {
            var hit = FindByName(child, name);
            if (hit != null) return hit;
        }

        return null;
    }
}