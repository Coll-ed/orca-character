using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Random;

namespace OrcaCharacter;

/// <summary>
///     ★★ <b>特殊死亡台词框架</b> —— 「死亡情况」的状态记录器（不碰 UI，只记账）。
///
///     <para><b>用户口径（2026-10-05）</b>：<i>"我要加几个特殊死亡台词。比如生死一线，
///     玩焚烧流派的时候偶尔出现把自己和敌人烧死的情况，这个时候的文本就是
///     『银龙奥卡与敌人名字一同被火焰烧成了灰烬』"</i>，并补充
///     <i>"这个文案在模组设置里面也是自己可以改的"</i> ⇒ 本类负责**认情况 + 记名字 + 出文案**，
///     只把这最后一句话交给结束画面补丁（<c>OrcaGameOverDeathQuotePatch</c>）去写。</para>
///
///     <para><b>为什么需要"批次号"</b>：判定「同归于尽」不能只看"玩家死了 + 敌人也死了"——
///     那会把"被敌人打死的同一回合里焚烧补刀"也算进来。所以给 <b>每一次焚烧波及</b>编号：
///     波及**之前**先快照"这一批里哪些带焚烧的敌人还活着"，出伤之后引擎把这一批的死者
///     一次性交给 <c>CreatureCmd.Kill</c>。只有在<b>同一个批次</b>里"快照里的敌人"与"玩家"同时死亡，
///     才判为「与敌同归于尽·焚烧」✓
///     （实据：反编译 <c>CreatureCmd.Damage</c> 末尾把所有本批死者合成一次
///     <c>Kill(killedCreatures)</c> 调用 —— 所以"同一次波及的死者在同一批"是引擎保证的）。</para>
///
///     <para><b>怎么加新的死亡情况</b>（扩展位）：
///     <list type="number">
///       <item>在本类里加一个新的 <c>record</c>（照 <see cref="KillRecord" /> 的样子，
///         只记"认这种情况"需要的最小事实）；</item>
///       <item>找一个"事实已经成立、且还没被清掉"的挂点写记录方法（焚烧走批次号，别的死法
///         可以用 <c>Hook.AfterDeath</c>／<c>CreatureCmd.SetCurrentHp</c> 之类）；</item>
///       <item>在 <see cref="TryBuild" /> 里加一个 <c>if</c> 分支：成立就返回该情况的文案；</item>
///       <item>在 <see cref="OrcaConfig" /> 里加对应的 <c>CustomXxx</c> 设置 + <c>RawFor</c> 分支
///         （分类名走 <see cref="KindBurnTogether" /> 那样的具名常量，代码里不散落字面量）。</item>
///     </list>
///     目前**只把「与敌同归于尽·焚烧」做扎实**，其余留空位。</para>
/// </summary>
internal static class OrcaDeathQuotes
{
    /// <summary>「与敌同归于尽·焚烧」这种情况的分类名（进设置取文案用）。</summary>
    internal const string KindBurnTogether = "burnDeath";

    /// <summary>
    ///     占位符：会被替换成**逗号分隔的敌人显示名列表**（例：<c>史莱姆, 酸液史莱姆</c>）。
    ///     玩家填文案时照写这个即可 —— 说明同步写在 <see cref="OrcaConfig.CustomBurnDeath" /> 的注释里。
    /// </summary>
    internal const string EnemiesPlaceholder = "{enemies}";

    /// <summary>敌人名之间的分隔符（用户口径：「同时烧死多个敌人 ⇒ 列名字、逗号分隔」）。</summary>
    private const string NameSeparator = ", ";

    /// <summary>一批（一次焚烧波及结算）的编号；0 = 还没开过批。</summary>
    private static int _batchSeq;

    /// <summary>最新一批"带焚烧的存活敌人"的名字快照（在造成伤害**之前**记下）。</summary>
    private static readonly List<string> _latestBatchBurningEnemies = new();

    /// <summary>最新一批的编号（用来把 <see cref="KillRecord" /> 与上面的快照对上）。</summary>
    private static int _latestBatchId;

    /// <summary>最近一次"本批死者里有玩家"的记录；没有就是 <c>null</c>。</summary>
    private static KillRecord? _playerDeathBatch;

    /// <summary>
    ///     ★★ <b>正在出伤的焚烧批次号</b>（<c>0</c> = 当前不在焚烧出伤里）。
    ///
    ///     <para>由 <see cref="BeginBurnDamage" /> / <see cref="EndBurnDamage" /> 在
    ///     <c>OrcaBurnPower</c> 调 <c>CreatureCmd.Damage</c> 的**前后夹住** ⇒ 这期间引擎报上来的
    ///     死亡批次就是"被这次焚烧烧死的那一批"，其中的玩家就是**被烧死的**（而不是被敌人打死的）✓
    ///     —— 这是"同归于尽"判定的关键，不能靠出伤之后再看 <c>IsDead</c>（见 <see cref="NoteKillBatch" /> 的注释）。</para>
    /// </summary>
    private static int _burnBatchInFlight;

    /// <summary>
    ///     ★ <b>已经交付给某个结束画面标签的自定义文案</b>（按 Godot 节点实例 id 记）。
    ///
    ///     <para>为什么需要它：同一个结束画面的**第二页**会**再写一次** <c>_deathQuote.Text</c>
    ///     （引擎把它换成 <c>_encounterQuote</c>，见反编译 <c>NGameOverScreen.AnimateInQuote</c> L542）⇒
    ///     没有这条记忆，我们第一页替换好的文案会被引擎覆盖回原文 ✗。</para>
    ///
    ///     <para>用**实例 id** 而不是节点引用：① 不持有 Godot 对象（不会吊住已释放的节点）；
    ///     ② 新一局的结束画面是新节点 ⇒ 实例 id 不同 ⇒ 天然不会命中上一局的记忆 ✓。</para>
    /// </summary>
    private static ulong _deliveredLabelId;

    /// <summary>交付给 <see cref="_deliveredLabelId" /> 那个标签的文案（同上）。</summary>
    private static string? _deliveredText;

    /// <summary>一批死者的最小事实快照。</summary>
    /// <param name="BatchId">这一批属于哪次焚烧波及（0 = 与焚烧无关的死亡）。</param>
    /// <param name="PlayerName">玩家的显示名（这一批里死了玩家才有值）。</param>
    /// <param name="BurnedEnemyNames">
    ///     同批死掉、且**出伤前**带焚烧的敌人显示名（按引擎给的顺序，已去重）——
    ///     在登记时就与快照求好交集，免得读的时候快照已被下一批覆盖。
    /// </param>
    private readonly record struct KillRecord(int BatchId, string PlayerName, IReadOnlyList<string> BurnedEnemyNames);

    // ══════════════════ 记录 ══════════════════

    /// <summary>
    ///     ★ 开一批新的焚烧波及：记下批号，并快照"这一批里**带焚烧的存活敌人**"。
    ///     必须在造成伤害**之前**调用 —— 出伤后引擎会把这批死者一次性交给 <c>CreatureCmd.Kill</c>。
    /// </summary>
    /// <param name="combat">当前战斗状态。</param>
    /// <returns>本批的编号；调用方随后用它把出伤夹在 <see cref="BeginBurnDamage" /> / <see cref="EndBurnDamage" /> 之间。</returns>
    internal static int NoteBurstSnapshot(ICombatState combat)
    {
        _latestBatchId = ++_batchSeq;
        _latestBatchBurningEnemies.Clear();

        try
        {
            foreach (var creature in combat.GetCreaturesOnSide(CombatSide.Enemy))
            {
                if (creature.IsDead) continue;
                if ((creature.GetPower<OrcaBurnPower>()?.Amount ?? 0) <= 0) continue;

                var name = DisplayNameOf(creature);
                if (name == null) continue;
                if (!_latestBatchBurningEnemies.Contains(name, StringComparer.Ordinal))
                    _latestBatchBurningEnemies.Add(name);
            }
        }
        catch (Exception ex)
        {
            // 边界显式（纪律 #4）：快照失败也留痕 —— 后果是这一批认不出"同归于尽"，不是静默错判。
            OrcaLog.Warn($"[Orca] 死亡台词：焚烧批次快照失败（本批按“无同归于尽”处理）：{ex.Message}", 2);
        }

        OrcaLog.Info($"[Orca] 死亡台词：焚烧批次 #{_latestBatchId}，带焚烧的存活敌人 {_latestBatchBurningEnemies.Count} 个", 2);
        return _latestBatchId;
    }

    /// <summary>
    ///     ★ <b>焚烧出伤开始</b>：由 <c>OrcaBurnPower</c> 在调 <c>CreatureCmd.Damage</c> **之前**调用，
    ///     把"接下来这一批死亡属于焚烧"这件事挂上。必须与 <see cref="EndBurnDamage" /> 成对（try/finally）。
    /// </summary>
    /// <param name="batchId"><see cref="NoteBurstSnapshot" /> 刚返回的批次号。</param>
    internal static void BeginBurnDamage(int batchId)
    {
        _burnBatchInFlight = batchId;
    }

    /// <summary>
    ///     ★ <b>焚烧出伤结束</b>：由 <c>OrcaBurnPower</c> 在 <c>CreatureCmd.Damage</c> **之后**（finally 里）调用。
    ///     闸门落回 0 ⇒ 之后任何与焚烧无关的死亡批次都会被正确记成 <c>BatchId = 0</c>（不替换文案）✓
    /// </summary>
    internal static void EndBurnDamage()
    {
        _burnBatchInFlight = 0;
    }

    /// <summary>
    ///     ★ 一批死亡**发生之前**登记（挂 <c>CreatureCmd.Kill</c> 的**前置**）。
    ///
    ///     <para>⚠️ <b>必须在前置、不能在后置</b>（反编译实据）：<c>CreatureCmd.Kill</c> 体内
    ///     L488 就 <c>NRun.Instance.ShowGameOverScreen(...)</c> 建结束画面，而结束画面的
    ///     <c>_Ready</c>（L470）**同步**调 <c>InitializeBannerAndQuote()</c>，在 L526 就把
    ///     <c>_deathQuote.Text</c> 写掉了 ⇒ 若等 <c>Kill</c> 返回之后才登记，替换时机已经错过，
    ///     玩家在结束画面上看到的仍是引擎原文 ✗（这正是第一版实现的实际缺陷）。</para>
    ///
    ///     <para>前置时生物尚未真正死亡，但本方法只读 <c>IsPlayer</c> / <c>Side</c>（与生死无关）✓；
    ///     且"这一批被处死的生物"就是引擎随后要处死的那个集合 —— <c>CreatureCmd.Damage</c> 收齐本批
    ///     死者后**一次性**调 <c>Kill(killedCreatures)</c>（L431）✓。</para>
    ///
    ///     <para>只在"这一批里有玩家"时留记录 —— 敌人单独死一批不构成"同归于尽"。
    ///     没玩家的批次直接返回（不动已有记录）；有玩家但**不是**死于焚烧的批次会把
    ///     <c>BatchId</c> 记成 0，顺带盖掉上一局可能残留的记录 ✓。</para>
    /// </summary>
    /// <param name="creatures">这一批被处死的生物（引擎原样给的集合）。</param>
    internal static void NoteKillBatch(IReadOnlyCollection<Creature> creatures)
    {
        try
        {
            if (creatures == null || creatures.Count == 0) return;

            Creature? player = null;
            var enemies = new List<string>();

            foreach (var creature in creatures)
            {
                if (creature == null) continue;

                if (creature.IsPlayer)
                {
                    player ??= creature;
                    continue;
                }

                if (creature.Side != CombatSide.Enemy) continue;

                var name = DisplayNameOf(creature);
                if (name == null) continue;
                if (!enemies.Contains(name, StringComparer.Ordinal)) enemies.Add(name);
            }

            if (player == null) return;

            // ★ 只有"这次 Kill 发生在焚烧出伤期间"才可能是被烧死的；否则 BatchId = 0（与焚烧无关）
            var batchId = _burnBatchInFlight;

            // ★ 在**登记时**就把"同批 + 出伤前带焚烧"的交集求好 —— 免得读的时候快照已被下一批覆盖
            var burnedEnemies = batchId == 0
                ? new List<string>()
                : enemies.Where(n => _latestBatchBurningEnemies.Contains(n, StringComparer.Ordinal)).ToList();

            var playerName = DisplayNameOf(player) ?? "奥卡";
            _playerDeathBatch = new KillRecord(batchId, playerName, burnedEnemies);

            if (batchId == 0)
            {
                OrcaLog.Info($"[Orca] 死亡台词：玩家「{playerName}」阵亡，但不是死于焚烧 ⇒ 用引擎原文", 2);
            }
            else
            {
                OrcaLog.Info($"[Orca] 死亡台词：玩家「{playerName}」在焚烧批次 #{batchId} 中阵亡（生死一线）"
                           + $"，同批被烧死的敌人 {burnedEnemies.Count} 个", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 死亡台词：死亡批次登记失败（本局不替换文案）：{ex.Message}", 2);
        }
    }

    // ══════════════════ 读取 ══════════════════

    /// <summary>
    ///     ★ 出"这一局结束画面该用的自定义死亡台词"；<c>null</c> = 不替换（保留引擎原文）。
    ///
    ///     <para>判「与敌同归于尽·焚烧」要**三个事实同时成立**：
    ///     ① 有"本批死者含玩家"的记录；
    ///     ② 那一批的 <c>BatchId</c> 非 0（＝那次 <c>Kill</c> 发生在焚烧出伤期间 ⇒ 玩家是被烧死的）；
    ///     ③ 同批死者里有**出伤前**带焚烧的敌人（＝真正一起被烧成灰的同伴）。
    ///     这样"被敌人打死 + 顺带死了个带焚烧的敌"不会被误判 ✓。</para>
    ///
    ///     <para>★ <b>同一个结束画面的第二页</b>会再写一次 <c>_deathQuote.Text</c>（引擎换成
    ///     <c>_encounterQuote</c>）⇒ 若这个标签（<paramref name="labelInstanceId" />）已经交付过，
    ///     这里把同一条文案再给一次，免得第一页替换好的文案又被引擎覆盖回原文 ✓。</para>
    ///
    ///     <para>文案来源：<see cref="OrcaConfig.RawFor" />("<c>burnDeath</c>") ⇒
    ///     <see cref="OrcaConfig.DeathQuoteCandidates" />（按「|」拆、随机抽一条）⇒
    ///     把 <see cref="EnemiesPlaceholder" /> 换成逗号分隔的敌人名。</para>
    ///
    ///     <para>拿不到敌人名 / 配置为空 ⇒ 返回 <c>null</c> 并**记日志**，绝不静默。</para>
    /// </summary>
    /// <param name="labelInstanceId">当前结束画面那个标签的 Godot 实例 id（用来认"是不是同一个画面"）。</param>
    /// <returns>可以直接写进结束画面标签的整句文案；或 <c>null</c>（用引擎原文）。</returns>
    internal static string? TryBuild(ulong labelInstanceId)
    {
        // ★ 同一个标签已经交付过 ⇒ 第二页照旧用它（否则会被引擎的 _encounterQuote 覆盖回原文）
        if (_deliveredText != null && _deliveredLabelId == labelInstanceId)
        {
            OrcaLog.Info("[Orca] 死亡台词：同一结束画面再次写文案 ⇒ 沿用已交付的自定义文案", 2);
            return _deliveredText;
        }

        var death = _playerDeathBatch;

        if (death == null)
        {
            OrcaLog.Info("[Orca] 死亡台词：本局没有“玩家阵亡”记录 ⇒ 用引擎原文", 2);
            return null;
        }

        if (death.Value.BatchId == 0)
        {
            OrcaLog.Info("[Orca] 死亡台词：玩家不是死在焚烧批次 ⇒ 用引擎原文", 2);
            return null;
        }

        var burnedEnemies = death.Value.BurnedEnemyNames;
        if (burnedEnemies.Count == 0)
        {
            // 边界显式：字面意义上"与敌同归于尽·焚烧"缺了敌人，就不替换。
            OrcaLog.Warn($"[Orca] 死亡台词：批次 #{death.Value.BatchId} 里玩家死于焚烧，但拿不到“被烧死的敌人名” ⇒ 用引擎原文", 2);
            return null;
        }

        var candidates = OrcaConfig.DeathQuoteCandidates(OrcaConfig.RawFor(KindBurnTogether));
        if (candidates == null || candidates.Length == 0)
        {
            OrcaLog.Warn("[Orca] 死亡台词：模组设置里「与敌同归于尽（焚烧）」没有可用文案 ⇒ 用引擎原文", 2);
            return null;
        }

        // ★ 用户口径"一次给 3 条，随机抽"：复用游戏自带的 Rng.Chaotic
        //   （实据：反编译 NGameOverScreen.InitializeBannerAndQuote 里抽 QUOTES 用的就是它）
        var template = Rng.Chaotic.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(template))
        {
            OrcaLog.Warn("[Orca] 死亡台词：抽到空白文案 ⇒ 用引擎原文", 2);
            return null;
        }

        var names = string.Join(NameSeparator, burnedEnemies);
        var text = template.Replace(EnemiesPlaceholder, names, StringComparison.Ordinal);

        if (!text.Contains(names, StringComparison.Ordinal))
        {
            // 玩家没写占位符（例如只写"我和他们一起烧成了灰"）—— 照用，但说清敌人名没进文本。
            OrcaLog.Info($"[Orca] 死亡台词：文案里没有 {EnemiesPlaceholder} 占位符 ⇒ 敌人名（{names}）不会出现在文本里", 2);
        }

        // ★ 记住"这个结束画面已经交付过"（第二页要用）。新一局的画面是新节点 ⇒ 实例 id 不同 ⇒ 不会误命中。
        _deliveredLabelId = labelInstanceId;
        _deliveredText = text;

        OrcaLog.Info($"[Orca] 死亡台词：命中「与敌同归于尽·焚烧」⇒ 替换结束画面文案（敌人：{names}）", 2);
        return text;
    }

    // ══════════════════ 清空 ══════════════════

    /// <summary>
    ///     ★ 清空"这一局死了谁"的记录（**每次结束画面新建**时调用）。
    ///     挂点：<c>NGameOverScreen._Ready</c> 的**后置** —— 结束画面新建都从干净状态开始，
    ///     免得上一局"同归于尽"的记录泄漏到本局（非焚烧死亡时错误替换文案）。
    ///
    ///     <para>★ <b>时序正好</b>：<c>_Ready</c> 体内就会写第一页文案（<c>InitializeBannerAndQuote</c>），
    ///     我们的**后置**在这之后才跑 ⇒ 本局的记录已经被那次写入用掉，清掉它不会误伤本局 ✓
    ///     （这也正是记录必须在 <c>Kill</c> **前置**登记的原因 —— 它得赶在结束画面建出来之前）。</para>
    ///
    ///     <para>⚠️ <b>刻意不清 <see cref="_deliveredText" /></b>：那个记忆要活到**同一画面的第二页**
    ///     （引擎会在第二页把文案换成 <c>_encounterQuote</c>），清掉就会让自定义文案被覆盖回原文 ✗。
    ///     它按节点实例 id 存 ⇒ 新一局的画面（新节点）自然命中不到，不需要清 ✓</para>
    /// </summary>
    internal static void Reset()
    {
        _latestBatchBurningEnemies.Clear();
        _latestBatchId = 0;
        _playerDeathBatch = null;
        OrcaLog.Info("[Orca] 死亡台词：记录已清空（新一局／新结束画面）；已交付文案的记忆保留给同一画面的第二页", 2);
    }

    // ══════════════════ 工具 ══════════════════

    /// <summary>取生物的**显示名**；取不到就返回 <c>null</c> 并记日志（不静默吞）。</summary>
    private static string? DisplayNameOf(Creature? creature)
    {
        if (creature == null) return null;

        try
        {
            var name = creature.Name;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            OrcaLog.Warn($"[Orca] 死亡台词：{creature.GetType().Name} 的显示名为空 ⇒ 这次不计入名字列表", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 死亡台词：取显示名出错（这次不计入名字列表）：{ex.Message}", 2);
        }

        return null;
    }
}
