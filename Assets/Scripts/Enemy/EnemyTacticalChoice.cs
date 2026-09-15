namespace Enemy
{
    /// <summary>
    /// 战术选择——Utility 评分后的结果，由 Chase 行为执行细节差异。
    ///
    /// 它不改变「是否战斗」这个大方向（那是 HFSM 的职责），
    /// 只决定「同为追击时怎么打」，避免又长出几个彼此难区分的状态。
    /// </summary>
    public enum EnemyTacticalChoice
    {
        /// <summary> 直接压上去，进入攻击距离就打（等于 4.1 之前的追击行为） </summary>
        Engage,

        /// <summary> 原地对峙：拉开一点距离，面向目标等待冷却 </summary>
        Hold,

        /// <summary> 绕到目标侧翼再切入 </summary>
        Flank,

        /// <summary> 残血或威胁过高：后撤拉开距离，边退边面向目标 </summary>
        Retreat
    }
}
