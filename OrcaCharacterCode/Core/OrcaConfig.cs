using BaseLib.Config;

namespace OrcaCharacter;

/// <summary>
///     ★★ 奥卡的模组配置 —— 显示在游戏的「**模组配置 / Mod Configuration**」里
///     （主菜单与设置里都能进）。BaseLib 的 <c>SimpleModConfig</c> 会自动生成配置界面、
///     自动读写落盘，我们只要声明**静态属性**。
///
///     <para>用户口径演进：
///     <list type="bullet">
///       <item>2026-09-23：「加一个日志是否开启的功能在设置里面」⇒ <see cref="VerboseLog" />；</item>
///       <item>2026-09-23：「加个**自定义预设与默认预设**：自定义预设就是让玩家写台词，
///         以及一个**弹出文本频率**」⇒ <see cref="Preset" /> / <see cref="Frequency" /> /
///         下面那组 <c>Custom*</c> 文本。</item>
///     </list></para>
///
///     <para>⚠️ 引入 BaseLib 是**用户明确拍板**的（原 csproj 写着"不依赖 BaseLib"）。
///     反编译/wiki 实据：<c>BaseLib.Config.ModConfigRegistry.Register(string modId, ModConfig config)</c>。</para>
///
///     <para>本地化键（见 <c>localization/zhs/settings_ui.json</c>）：<c>ORCACHARACTER.mod_title</c>
///     与 <c>ORCACHARACTER-&lt;属性名转大写下划线&gt;.title</c>。</para>
/// </summary>
internal class OrcaConfig : SimpleModConfig
{
    // ══════════════════ ① 日志 ══════════════════

    /// <summary>
    ///     是否输出 <c>[Orca]</c> 详细日志（默认**开**）。关掉后 <see cref="OrcaLog.Info" /> /
    ///     <see cref="OrcaLog.Warn" /> 静默，但 <see cref="OrcaLog.Error" /> **永远输出**。
    /// </summary>
    public static bool VerboseLog { get; set; } = true;

    // ══════════════════ ② 台词预设 ══════════════════

    /// <summary>台词来源：<b>默认预设</b>（我们写的，见 characters.json）或<b>自定义预设</b>（玩家自己写）。</summary>
    public enum BanterPreset
    {
        /// <summary>默认预设 —— 用 mod 内置的台词库。</summary>
        Default = 0,

        /// <summary>自定义预设 —— 用下面那组输入框里玩家写的台词（留空的分类自动回落到默认库）。</summary>
        Custom = 1,
    }

    /// <summary>★ 台词预设（默认 = 默认预设）。</summary>
    public static BanterPreset Preset { get; set; } = BanterPreset.Default;

    // ══════════════════ ③ 弹出文本频率 ══════════════════

    /// <summary>
    ///     气泡弹出频率 —— 直接改写"每场战斗能吃多少句"的额度（见 <c>OrcaSpeech</c>）。
    ///
    ///     <para>基础口径（用户 2026-09-17）是**每场 4 句**、卡牌台词 3 句；
    ///     这里按档位成比例缩放，**分类上限**（受伤/击杀/拒绝各 2 句）也跟着缩，
    ///     免得某个高频类别把额度吃光。</para>
    /// </summary>
    public enum BanterFrequency
    {
        /// <summary>关闭 —— 完全不出气泡（台词与卡牌台词都停）。</summary>
        Off = 0,

        /// <summary>少 —— 约基础的一半。</summary>
        Low = 1,

        /// <summary>标准 —— 用户原始口径（每场 4 句）。</summary>
        Normal = 2,

        /// <summary>多 —— 基础的两倍。</summary>
        High = 3,
    }

    /// <summary>★ 气泡频率（默认 = 标准）。</summary>
    public static BanterFrequency Frequency { get; set; } = BanterFrequency.Normal;

    // ══════════════════ ④ 自定义台词（Preset = Custom 时生效）══════════════════
    //    ★ 每个分类一行，**多句用「|」分隔**（例：上啊！|来吧。|别挡路。）。
    //      BaseLib 对 string 属性默认生成 LineEdit，不需要额外特性。
    //      留空 ⇒ 该分类自动回落到内置默认台词库（不会变成不说话）。

    /// <summary>战斗开场白。</summary>
    public static string CustomCombatStart { get; set; } = "";

    /// <summary>狂躁触发时的台词。</summary>
    public static string CustomFrenzy { get; set; } = "";

    /// <summary>受伤时的台词。</summary>
    public static string CustomHurt { get; set; } = "";

    /// <summary>击杀敌人时的台词。</summary>
    public static string CustomKill { get; set; } = "";

    /// <summary>龙剑/魔剑拒绝夺命（代价会致死、牌打不出去）时的台词。</summary>
    public static string CustomSwordRefuse { get; set; } = "";

    /// <summary>打出**攻击牌**时的台词。</summary>
    public static string CustomAttack { get; set; } = "";

    /// <summary>打出**防御牌**（会给格挡的技能牌）时的台词。</summary>
    public static string CustomDefend { get; set; } = "";

    /// <summary>打出其它**技能牌**时的台词。</summary>
    public static string CustomSkill { get; set; } = "";

    /// <summary>打出**能力牌**时的台词。</summary>
    public static string CustomPower { get; set; } = "";

    /// <summary>**受到重击**（一次挨掉很多血）时的台词。</summary>
    public static string CustomHeavyHurt { get; set; } = "";

    /// <summary>**成功挡下伤害**（完全格挡）时的台词。</summary>
    public static string CustomBlocked { get; set; } = "";

    /// <summary>**回血**时的台词。</summary>
    public static string CustomHeal { get; set; } = "";

    // ══════════════════ ⑤ 特殊死亡台词（★ 2026-10-05 新增）══════════════════
    //    ★ 与上面那组**不同的两点**（别混）：
    //      ① 出现在【游戏结束画面】的死亡台词，**不是**战斗里的说话气泡；
    //      ② 默认值**不是空串**，而是占位文案 —— 这样功能开箱即用（用户口径
    //         「默认值给占位文案，让功能开箱即用」）。玩家改成自己写的即可。

    /// <summary>
    ///     ★ <b>与敌同归于尽（焚烧）</b> —— 玩家和敌人被同一次【焚烧】一起烧死时，
    ///     结束画面显示这句，替换引擎原文。
    ///
    ///     <para><b>占位符</b>：<c>{enemies}</c> ⇒ 被替换成**逗号分隔的敌人显示名列表**
    ///     （例：<c>史莱姆, 酸液史莱姆</c>）。同时烧死多个敌人时会全部列出。</para>
    ///
    ///     <para><b>多句随机抽</b>：多条文案用「|」分隔（例：<c>甲。|乙。|丙。</c>），
    ///     每次死亡随机抽一条 —— 与其他 <c>Custom*</c> 是同一套分隔约定
    ///     （见 <see cref="Split" />）。</para>
    ///
    ///     <para><b>生效条件</b>：★ <b>不受「台词预设」影响，开箱即用</b> ——
    ///     与其他 <c>Custom*</c> 气泡台词**不同**（那些要先把 <see cref="Preset" /> 设为自定义，
    ///     因为留空时它们要回落到内置台词库；死亡台词没有"内置库"可回落）。
    ///     这里填什么就用什么，<b>留空</b>才表示"这种情况不替换、结束画面用引擎原文"。</para>
    /// </summary>
    public static string CustomBurnDeath { get; set; } = "银龙奥卡与{enemies}一同被火焰烧成了灰烬";

    // ══════════════════ 工具 ══════════════════

    /// <summary>全部台词分类（顺序与设置界面里输入框的顺序一致）。</summary>
    /// <remarks>
    ///     ★ 2026-10-05：**故意不把死亡台词（<see cref="CustomBurnDeath" />）加进来** ——
    ///     这个数组唯一的消费方是 <c>OrcaSpeech.InjectCustomLines</c>（把气泡台词注入本地化表），
    ///     死亡台词**不走本地化**、也不该被气泡系统当成候选。它只需要
    ///     <see cref="RawFor" /> 有分支即可。
    /// </remarks>
    internal static readonly string[] Categories =
    {
        "combatStart", "frenzy", "hurt", "kill", "swordRefuse",
        "attack", "defend", "skill", "power",
        "heavyHurt", "blocked", "heal",
    };

    /// <summary>取某个分类的自定义台词原文（没写就是空串）。</summary>
    internal static string RawFor(string category) => category switch
    {
        "combatStart" => CustomCombatStart,
        "frenzy" => CustomFrenzy,
        "hurt" => CustomHurt,
        "kill" => CustomKill,
        "swordRefuse" => CustomSwordRefuse,
        "attack" => CustomAttack,
        "defend" => CustomDefend,
        "skill" => CustomSkill,
        "power" => CustomPower,
        "heavyHurt" => CustomHeavyHurt,
        "blocked" => CustomBlocked,
        "heal" => CustomHeal,
        // ★ 2026-10-05：特殊死亡台词（结束画面）—— 分类名同 OrcaDeathQuotes.KindBurnTogether
        OrcaDeathQuotes.KindBurnTogether => CustomBurnDeath,
        _ => "",
    };

    /// <summary>
    ///     把一条自定义台词串按「|」拆成若干句；返回 <c>null</c> 表示"该分类用默认库"。
    ///     <para>放在配置类里，让 <c>OrcaSpeech</c> 只管取用、不重复解析逻辑。</para>
    /// </summary>
    internal static string[]? Split(string raw)
    {
        if (Preset != BanterPreset.Custom) return null;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Where(s => s.Length > 0)
                       .ToArray();
        return parts.Length > 0 ? parts : null;
    }

    /// <summary>
    ///     ★ 拆一条**死亡台词**（给结束画面用）。
    ///
    ///     <para>分隔约定与 <see cref="Split" /> 完全一致（<c>|</c>），但**不看**
    ///     <see cref="Preset" /> 那一档：死亡台词只走这一个设置，没有"内置默认库"可回落 ——
    ///     留空就是"这种情况不替换文案"（与气泡台词留空＝回落内置库的语义不同，故单列一个方法，
    ///     而不是把 <see cref="Split" /> 改成带开关的版本）。</para>
    /// </summary>
    /// <param name="raw">设置里的原文。</param>
    /// <returns>候选文案；没有可用候选时返回 <c>null</c>。</returns>
    internal static string[]? DeathQuoteCandidates(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Where(s => s.Length > 0)
                       .ToArray();
        return parts.Length > 0 ? parts : null;
    }

    /// <summary>按频率档位缩放一个额度（保证至少 1，除非是"关闭"）。</summary>
    internal static int Scale(int baseAmount) => Frequency switch
    {
        BanterFrequency.Off => 0,
        BanterFrequency.Low => Math.Max(1, baseAmount / 2),
        BanterFrequency.High => baseAmount * 2,
        _ => baseAmount,
    };
}