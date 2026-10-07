namespace OrcaCharacter;

/// <summary>
///     ★★ <b>「这一笔回血属于【吸血】」的闸门</b> —— 归墟靠它区分"谁的治疗该被拦下"。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L52-54，2026-10-07 改版）：
///     <i>"归墟 / 2费，魔剑，蓝卡，能力牌，敲后75%比例 /
///     场上所有角色无法回复生命，回复的生命按50%比列（向下取整）转入魔剑的附加伤害，<b>不拦截吸血</b>"</i>
///     ⇒ 归墟拦下"其它一切回复"，但<b>【吸血】那一路照常回血</b>。</para>
///
///     <para><b>为什么必须有一个显式闸门</b>：归墟的拦截挂在 <c>CreatureCmd.Heal</c> 的 Prefix 上
///     （实据见 <see cref="OrcaVoidReturnBlockPatch" /> —— 那条路已被焚文的治疗加成验证有效），
///     而该方法的签名只有 <c>(Creature, decimal, bool)</c>，<b>不带任何"来源"信息</b>
///     （反编译实据：<c>CreatureCmd.cs:738</c>）⇒ 调用方必须自己在前后夹一个标记。
///     本工程已有同款做法（<c>OrcaDeathQuotes.BeginBurnDamage</c> 夹焚烧出伤、
///     <c>OrcaCombatHp.Suppress</c> 夹自己改上限），此处沿用同一套。</para>
///
///     <para><b>谁 Begin</b>（生产端，两处，都是"吸血"语义）：
///     <list type="bullet">
///       <item><see cref="OrcaLifestealPower.AfterCardPlayed" /> —— 出牌合计后的那次回血；</item>
///       <item><see cref="OrcaBloodBlade.HealFromSingleTarget" /> —— 魔剑单敌狂躁的"本牌自愈"
///         （权威口径：单敌时吸血倍率 100% ⇔ 回血 = 伤害本身）。</item>
///     </list>
///     <b>谁读</b>（消费端，两处，都必须放行）：<see cref="OrcaVoidReturnBlockPatch" /> 的 Prefix
///     与 <see cref="OrcaVoidReturnPower.AfterCurrentHpChanged" /> 的兜底撤销。</para>
///
///     <para>★ <b>用计数而不是布尔</b>：配对写法是 <c>try { Begin(); … } finally { End(); }</c>，
///     嵌套也不会提前把闸门落下；<c>End</c> 多于 <c>Begin</c> 时记 Warn（不静默）。</para>
/// </summary>
internal static class OrcaLifestealHeal
{
    /// <summary>闸门嵌套深度（&gt; 0 ⇒ 当前这一笔回血来自【吸血】）。</summary>
    private static int _depth;

    /// <summary>当前这次回血是否由【吸血】发起。</summary>
    internal static bool InFlight => _depth > 0;

    /// <summary>进入"这一笔回血属于【吸血】"的区间（必须与 <see cref="End" /> 配对）。</summary>
    internal static void Begin() => _depth++;

    /// <summary>离开该区间 —— 必须在 <c>finally</c> 里调用，异常时也不漏落闸门。</summary>
    internal static void End()
    {
        if (_depth > 0)
        {
            _depth--;
            return;
        }

        // 落到这里说明调用方少配了一次 Begin（例如异常路径写错）⇒ 显式记录，不静默。
        OrcaLog.Warn("[Orca] 吸血回血闸门：End 多于 Begin（闸门已归零，请检查配对写法）");
    }
}
