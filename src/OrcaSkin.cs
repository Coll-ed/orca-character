using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace OrcaCharacter;

/// <summary>
///     银龙奥卡的皮肤状态（板甲 / 婚纱）。
///
///     ★ 切换入口＝**奥卡皮肤包自带的那个皮肤管理器**（CharacterSkinManager，选人界面里的面板），
///       我们不自己造 UI、也不设游戏热键；它把选择写在
///       <c>user://mods/character_skin_manager/orca.txt</c>（纯文本，一行 skin_id）。
///       本类只**读这个文本文件**（读的是数据，不是调用它的代码），据此决定：
///         · 打架/选人/商店/篝火用哪套骨架（两套都自带在我们包里）；
///         · 卡面边框材质（板甲＝偏黑灰 / 婚纱＝白）；
///         · 能量美术叠色（板甲＝偏黑灰 / 婚纱＝白）。
///
///     兜底：管理器不在（或没选过）时，读我们模组目录下的 <c>orca_skin.json</c>：
///       <c>{ "active": "plate" | "wedding" }</c>，默认板甲。这样本角色**不依赖任何第三方**也能跑。
/// </summary>
internal static class OrcaSkin
{
    public const string Plate = "plate";
    public const string Wedding = "wedding";

    /// <summary>兜底配置文件（管理器缺席时用）。</summary>
    private const string FallbackFileName = "orca_skin.json";

    /// <summary>皮肤管理器的选择落盘位置（user:// 下）。</summary>
    private const string ManagerDir = "mods/character_skin_manager";

    private const string ManagerFile = "orca.txt";

    private static string _active = Plate;
    private static string _modDir = string.Empty;
    private static string _source = "默认";
    private static string _logged = string.Empty;

    /// <summary>节流：上次真正读盘的时间戳（毫秒）＋重入保护（Refresh 内部会读 FrameMaterialName 打日志）。</summary>
    private const int RefreshThrottleMs = 1500;
    private static long _lastRefreshMs;
    private static bool _refreshing;

    /// <summary>当前皮肤：<see cref="Plate" /> 或 <see cref="Wedding" />（访问时自动同步一次管理器选择）。</summary>
    public static string Active
    {
        get { RefreshThrottled(); return _active; }
    }

    public static bool IsWedding
    {


        get { RefreshThrottled(); return _active == Wedding; }
    }

    /// <summary>这一选择是从哪儿来的（日志用）。</summary>
    public static string Source => _source;

    // ── 骨架路径（两套都在我们自己的包里）────────────────────
    public static string BattleSkeleton =>
        IsWedding
            ? "res://animations/Orcaweddingdress/characters/ironclad/ironclad_skel_data.tres"
            : "res://animations/orca_plate/characters/ironclad/ironclad_skel_data.tres";

    public static string CharSelectSkeleton =>
        IsWedding
            ? "res://animations/Orcaweddingdress/character_select/ironclad/characterselect_ironclad_skel_data.tres"
            : "res://animations/orca_plate/character_select/ironclad/characterselect_ironclad_skel_data.tres";

    public static string MerchantSkeleton =>
        IsWedding
            ? "res://animations/Orcaweddingdress/merchant/ironclad/ironclad_merchant_skel_data.tres"
            : "res://animations/orca_plate/merchant/ironclad/ironclad_merchant_skel_data.tres";

    public static string RestSkeleton =>
        IsWedding
            ? "res://animations/Orcaweddingdress/rest_site/ironclad/rest_site_ironclad_skel_data.tres"
            : "res://animations/orca_plate/rest_site/ironclad/rest_site_ironclad_skel_data.tres";

    // ── 卡框材质（B 卡面边框随皮肤）────────────────────────
    /// <summary>婚纱 = 白（card_frame_silver）；板甲 = 偏黑灰（card_frame_gunmetal）。两份材质都由我们自己带。</summary>
    public static string FrameMaterialName
    {
        get
        {
            RefreshThrottled();
            return _active == Wedding ? "card_frame_silver" : "card_frame_gunmetal";
        }
    }

    // ── 战斗能量球换色材质（板甲＝灰黑 / 婚纱＝红白）────────
    /// <summary>
    ///     战斗里「左边那颗能量球」+ 它周围的光火特效用的换色材质（我们自己的 shader：按亮度重映射）。
    ///     素材仍是原来那套（战士的红色能量），只换配色。
    /// </summary>
    public static string EnergyOrbMaterialPath
    {
        get
        {
            RefreshThrottled();
            return _active == Wedding
                ? "res://materials/ui/orca_orb_wedding_mat.tres"
                : "res://materials/ui/orca_orb_plate_mat.tres";
        }
    }

    /// <summary>
    ///     外框（<c>LayerRing</c>）用的**扫光材质**：和能量球同一套配色，外加"每段依次变白→半透明→回原色"的扫光。
    ///     不扫的时候（<c>sweep_strength = 0</c>）它就等于普通换色材质。
    /// </summary>
    public static string RingSweepMaterialPath
    {
        get
        {
            RefreshThrottled();
            return _active == Wedding
                ? "res://materials/ui/orca_ring_sweep_wedding_mat.tres"
                : "res://materials/ui/orca_ring_sweep_plate_mat.tres";
        }
    }

    /// <summary>我们自带的、需要主动登记进游戏预载缓存的素材
    ///     （卡框 2 + 能量球 2 + 外框扫光 2 + 卡牌能量图标 4 + 遗物图 2 + 婚纱角色小图 1 + 地图标记 1 + 吸血 buff 图 1
    ///      + 主界面选择音效 2）。</summary>
    public static readonly string[] CustomAssetPaths =
    {
        // ★ 主界面选择音效（板甲 / 婚纱两版，用户自备成品 —— 见 Orca.CharacterSelectSfx）
        "res://OrcaCharacter/audio/orca_select_plate.tres",
        "res://OrcaCharacter/audio/orca_select_wedding.tres",
        "res://materials/cards/frames/card_frame_gunmetal_mat.tres",
        "res://materials/cards/frames/card_frame_silver_mat.tres",
        "res://materials/ui/orca_orb_plate_mat.tres",
        "res://materials/ui/orca_orb_wedding_mat.tres",
        "res://materials/ui/orca_ring_sweep_plate_mat.tres",
        "res://materials/ui/orca_ring_sweep_wedding_mat.tres",
        "res://images/ui/card/energy_orca.png",
        "res://images/ui/card/energy_orca_text.png",
        "res://images/ui/card/energy_orca_wedding.png",
        "res://images/ui/card/energy_orca_wedding_text.png",
        "res://images/relics/orca_horn.png",
        "res://images/relics/orca_horn_big.png",
        "res://images/ui/top_panel/character_icon_orca_wedding.png",
        "res://images/packed/character_select/char_select_orca_wedding.png",
        "res://images/packed/map/icons/map_marker_orca.png",
        // ★「吸血」buff 图标 —— 套用**原版荆棘(Thorns)图标**，重上色为**深血红色**
        //   （由 _verify/make_lifesteal_icon.py 解出原版 S3TC 像素后重上色生成）
        "res://images/powers/orca_lifesteal_power.png",
        "res://images/atlases/power_atlas.sprites/orca_lifesteal_power.tres",
        // ★「生死一线」buff 图标 —— **用户自备**（`奥卡卡包集\\卡牌包2\\熔渊图标.png`，橙红熔岩徽记），
        //   由 _verify/make_molten_bone_icon.py 缩放成 64×64 并生成四件套（ctex + png + .import + atlas.tres）
        "res://images/powers/orca_molten_bone_power.png",
            "res://images/powers/orca_ember_wing_power.png",
            "res://images/powers/orca_ember_wing_power_wedding.png",
            "res://images/powers/orca_killing_intent_power_wedding.png",
            "res://images/powers/orca_wisdom_power_wedding.png",
            "res://images/powers/orca_lineage_power_wedding.png",
            "res://images/powers/orca_blood_nirvana_power.png",
            "res://images/powers/orca_crimson_temper_power.png",
            "res://images/powers/orca_dragon_dignity_power.png",
            "res://images/powers/orca_void_return_power.png",
            "res://images/powers/orca_homestead_power.png",
            "res://images/powers/orca_extra_turn_power.png",
            "res://images/atlases/power_atlas.sprites/orca_blood_nirvana_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_crimson_temper_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_dragon_dignity_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_void_return_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_homestead_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_extra_turn_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_ember_wing_power_wedding.tres",
            "res://images/atlases/power_atlas.sprites/orca_killing_intent_power_wedding.tres",
            "res://images/atlases/power_atlas.sprites/orca_wisdom_power_wedding.tres",
            "res://images/atlases/power_atlas.sprites/orca_lineage_power_wedding.tres",
            "res://images/powers/orca_killing_intent_power.png", "res://images/powers/orca_wisdom_power.png", "res://images/powers/orca_lineage_power.png", "res://images/atlases/power_atlas.sprites/orca_killing_intent_power.tres", "res://images/atlases/power_atlas.sprites/orca_wisdom_power.tres", "res://images/atlases/power_atlas.sprites/orca_lineage_power.tres",
        "res://images/atlases/power_atlas.sprites/orca_molten_bone_power.tres",
            "res://images/atlases/power_atlas.sprites/orca_ember_wing_power.tres"
    };

    // ── 配色（左边能量美术叠色用；与卡框同源）───────────────
    /// <summary>婚纱 = 接近纯白；板甲 = 偏黑的灰。用于给能量计数器图层叠 modulate。</summary>
    public static Godot.Color Tint
    {
        get
        {
            RefreshThrottled();
            return _active == Wedding
                ? new Godot.Color(0.97f, 0.97f, 1.00f)
                : new Godot.Color(0.34f, 0.35f, 0.38f);
        }
    }

    // ── 读 ─────────────────────────────────────────────────
    public static void Init(string modDir)
    {
        _modDir = modDir;
        Refresh();
        OrcaLog.Info($"[Orca] 皮肤 = {_active}（来源：{_source}）；管理器文件={ManagerStorePath()}", 2);
    }

    /// <summary>
    ///     重新读一次当前皮肤 —— 进战斗 / 进选人前各调一次，
    ///     这样在皮肤管理器面板里切完，下一次进战斗就是新皮肤。
    /// </summary>
    public static void Refresh()
    {
        _lastRefreshMs = Environment.TickCount64;
        var byManager = ReadManagerSelection();
        if (byManager != null)
        {
            Set(byManager, "皮肤管理器");
            return;
        }

        var byFile = ReadFallbackFile();
        if (byFile != null)
        {
            Set(byFile, FallbackFileName);
            return;
        }

        Set(Plate, "默认（板甲）");
    }

    /// <summary>
    ///     节流版 <see cref="Refresh" />：卡框材质 / 叠色这些属性每帧都可能被读，
    ///     所以 1.5 秒窗口内只真正读一次盘 —— 皮肤管理器里切完，最迟 1.5 秒后全场景（手牌、牌库、奖励）都跟着变。
    /// </summary>
    private static void RefreshThrottled()
    {
        if (_refreshing) return;
        if (Environment.TickCount64 - _lastRefreshMs < RefreshThrottleMs) return;
        _refreshing = true;
        try { Refresh(); }
        finally { _refreshing = false; }
    }

    private static void Set(string skin, string source)
    {
        var changed = _active != skin;
        _active = skin;
        _source = source;
        var tag = $"{skin}/{source}";
        if (tag == _logged) return;
        _logged = tag;
        OrcaLog.Info($"[Orca] 当前皮肤 → {skin}（{source}）卡框={FrameMaterialName} 骨架={BattleSkeleton}", 2);

        // ★ 皮肤真的变了 ⇒ 把**已在屏上**的角色小图就地换成新皮肤那张
        //   （选人界面最下面那排小图是建屏时读一次的，不主动刷新就不会变 —— 实机验证过）
        if (changed) OrcaCharacterIcon.RefreshLiveIcons();
    }

    private static string ManagerStorePath()
    {
        try
        {
            var userDir = Godot.OS.GetUserDataDir();
            if (string.IsNullOrWhiteSpace(userDir)) return "(user:// 不可用)";
            return Path.Combine(userDir, ManagerDir.Replace('/', Path.DirectorySeparatorChar), ManagerFile);
        }
        catch
        {
            return "(user:// 不可用)";
        }
    }

    /// <summary>读皮肤管理器的选择（纯文本一行 skin_id）。读不到就返回 null。</summary>
    private static string? ReadManagerSelection()
    {
        try
        {
            var path = ManagerStorePath();
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path).Trim();
            if (text.Length == 0) return null;

            if (text.Contains("wedding", StringComparison.OrdinalIgnoreCase)) return Wedding;
            if (text.Contains("plate", StringComparison.OrdinalIgnoreCase)) return Plate;

            OrcaLog.Warn($"[Orca] 皮肤管理器选了不认识的皮肤「{text}」，忽略。", 2);
            return null;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 读皮肤管理器选择失败：{ex.Message}", 2);
            return null;
        }
    }

    /// <summary>兜底：我们自己模组目录下的 orca_skin.json。</summary>
    private static string? ReadFallbackFile()
    {
        try
        {
            if (string.IsNullOrEmpty(_modDir)) return null;
            var path = Path.Combine(_modDir, FallbackFileName);
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("active", out var v)) return null;
            var s = v.GetString();
            return s is Wedding or Plate ? s : null;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 读 {FallbackFileName} 失败：{ex.Message}", 2);
            return null;
        }
    }
    /// <summary>
    ///     ★★★ **全项目唯一的"按皮肤二选一"入口**（用户 2026-09-23：「我最看重的就是代码优雅简洁好维护」）。
    ///
    ///     <para>重构前 <c>IsWedding ? A : B</c> 这种三元式**散落在 7 个文件、13 处**
    ///     （能量球图标 / 数值高亮色 / 地图画线 / 能量标签描边 / 选人立绘 / 能量球图层 / 说话气泡），
    ///     其中说话气泡那两处还是**逐字重复**的同一段逻辑。
    ///     ⇒ 全部收敛到这里：**以后要加第三套皮肤，只改这一个方法**。</para>
    ///
    ///     <para>命名按"板甲在前、婚纱在后"，读起来就是"板甲用这个，婚纱用那个"。</para>
    /// </summary>
    internal static T BySkin<T>(T plate, T wedding) => IsWedding ? wedding : plate;

    /// <summary>说话气泡颜色（板甲＝灰黑 / 婚纱＝红）—— 原来在 <c>Orca.cs</c> 与 <c>OrcaSpeech.cs</c> 各写了一遍。</summary>
    internal static MegaCrit.Sts2.Core.Nodes.Vfx.VfxColor BubbleColor =>
        BySkin(MegaCrit.Sts2.Core.Nodes.Vfx.VfxColor.DarkGray, MegaCrit.Sts2.Core.Nodes.Vfx.VfxColor.Red);
}