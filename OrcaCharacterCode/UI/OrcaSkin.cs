using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace OrcaCharacter;

/// <summary>
///     银龙奥卡的皮肤状态（板甲 / 婚纱）。
///
///     ★ 切换入口＝**本模组自带的皮肤面板**（<see cref="OrcaSkinPanel" />，选人界面右下角那块，
///       只在选中奥卡时显示）。用户口径 2026-10-05：「下一步就是重新做皮肤切换管理器了」
///       —— 原先入口在**外部皮肤包**（<c>奥卡皮肤-Orca\CharacterSkinManager</c>）手里，
///       本角色离了它就没法切皮肤；现在整合进本模组本体。
///
///     选择的**读写**（用户口径 2026-10-05）：
///       · 读：本模组配置 &gt; 外部管理器文件（兼容旧选择）&gt; <c>orca_skin.json</c> &gt; 默认板甲；
///       · 写（「双写」）：写本模组配置的同时**也写外部管理器那份**，两边永远同值、不会各切各的。
///
///     本类据此决定：打架/选人/商店/篝火用哪套骨架（两套都自带在我们包里）、
///     卡面边框材质（板甲＝偏黑灰 / 婚纱＝白）、能量美术叠色（板甲＝偏黑灰 / 婚纱＝白）。
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

    // ── 本模组自己的选择（用户口径 2026-10-05：「优先读自带的配置」）────────────
    //   读的优先级：本模组配置 > 外部管理器文件（兼容/迁移旧选择）> orca_skin.json > 默认板甲
    //   写的策略（用户口径 2026-10-05「双写」）：写本模组配置的同时**也写外部管理器那份**，
    //   这样外部面板与本模组永远读到同一个值，不会各切各的、互相打架。

    /// <summary>本模组自己的选择落盘位置（user:// 下）。目录名与模组 id 同源。</summary>
    private const string StoreDir = "mods/orca_character";

    private const string StoreFile = "skin.txt";

    /// <summary>
    ///     皮肤在**外部管理器**里的 skin_id（双写它的 <c>orca.txt</c> 时用）。
    ///
    ///     <para>★ 单一来源＝<c>skins/orca/&lt;皮肤&gt;/skin.json</c> 的 <c>skin_id</c> 字段
    ///     （外部管理器就是按它认皮肤的，反编译实据：<c>CharacterSkinSelectionStore.GetSelectedSkinId</c>
    ///     拿到 id 后与 <c>CharacterSkinDefinition.SkinId</c> 比对）⇒ 改那两份 json 时这里必须同步。</para>
    /// </summary>
    private const string ManagerIdPlate = "Orca_PlateMail";

    private const string ManagerIdWedding = "Orca_Weddingdress";

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

        // ① 本模组自己的配置 —— 用户口径 2026-10-05：「优先读自带的配置」
        var byOwn = ReadOwnStore();
        if (byOwn != null)
        {
            Set(byOwn, "本模组配置");
            return;
        }

        // ② 外部皮肤包的管理器 —— 兼容旧选择（本模组还没写过、或玩家一直用它的面板切）
        var byManager = ReadManagerSelection();
        if (byManager != null)
        {
            Set(byManager, "皮肤管理器");
            return;
        }

        // ③ 兜底文件 → ④ 默认
        var byFile = ReadFallbackFile();
        if (byFile != null)
        {
            Set(byFile, FallbackFileName);
            return;
        }

        Set(Plate, "默认（板甲）");
    }

    /// <summary>
    ///     ★ **切换皮肤**（选人界面那块面板点按钮走这里）。
    ///
    ///     <para><b>双写</b>（用户口径 2026-10-05）：写本模组自己的配置**同时**写外部管理器的
    ///     <c>orca.txt</c> ⇒ 两边永远同一个值，不会各切各的。</para>
    ///
    ///     <para>返回值＝皮肤**是否真的变了**（没变就不必重套外观）。</para>
    /// </summary>
    internal static bool SetSkin(string skin)
    {
        if (skin != Plate && skin != Wedding)
        {
            OrcaLog.Warn($"[Orca] 切皮肤：不认识的皮肤「{skin}」，忽略", 2);
            return false;
        }

        var changed = _active != skin;

        // ★ 先落盘再改内存态：写失败也要让本次会话切过去（否则界面点了没反应，更难查）
        WriteOwnStore(skin);
        WriteManagerFile(skin);

        Set(skin, "本模组配置");
        OrcaLog.Info($"[Orca] 皮肤已切换 → {skin}（已双写：本模组配置 + 管理器文件）", 2);
        return changed;
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

    /// <summary>Godot 虚拟路径 <c>user://</c> 下某个文件的真实路径（hardcode-ok：这是引擎的虚拟路径协议名，不是本机路径）；不可用时返回 null。</summary>
    private static string? UserPath(string dir, string file)
    {
        try
        {
            var userDir = Godot.OS.GetUserDataDir();
            if (string.IsNullOrWhiteSpace(userDir)) return null;
            return Path.Combine(userDir, dir.Replace('/', Path.DirectorySeparatorChar), file);
        }
        catch
        {
            return null;
        }
    }

    private static string ManagerStorePath() => UserPath(ManagerDir, ManagerFile) ?? "(不可用)";

    /// <summary>读本模组自己的选择（一行 skin_id）。读不到就返回 null。</summary>
    private static string? ReadOwnStore()
    {
        try
        {
            var path = UserPath(StoreDir, StoreFile);
            if (path == null || !File.Exists(path)) return null;

            var text = File.ReadAllText(path).Trim();
            return text is Plate or Wedding ? text : null;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 读本模组皮肤配置失败：{ex.Message}", 2);
            return null;
        }
    }

    /// <summary>写本模组自己的选择。失败只记日志（不影响本次会话已生效的切换）。</summary>
    private static void WriteOwnStore(string skin)
    {
        try
        {
            var path = UserPath(StoreDir, StoreFile);
            if (path == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, skin);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 写本模组皮肤配置失败：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     同步写外部管理器那份（<c>orca.txt</c>）。
    ///     ⚠️ 内容必须是**管理器的 skin_id**（<c>Orca_PlateMail</c> / <c>Orca_Weddingdress</c>），
    ///     不能写我们自己的 <c>plate</c> / <c>wedding</c> —— 它按自己的定义表比对 id。
    /// </summary>
    private static void WriteManagerFile(string skin)
    {
        try
        {
            var path = UserPath(ManagerDir, ManagerFile);
            if (path == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, skin == Wedding ? ManagerIdWedding : ManagerIdPlate);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 同步写皮肤管理器文件失败（外部面板可能不跟随）：{ex.Message}", 2);
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