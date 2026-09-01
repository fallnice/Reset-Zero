namespace Combat
{
    /// <summary>
    /// 战斗相关 Blackboard 键名常量——避免魔法字符串散落各处
    /// </summary>
    public static class CombatKeys
    {
        /// <summary> 攻击力倍率（1.0 = 无加成，1.15 = +15%） </summary>
        public const string AttackMultiplier = "Combat_AttackMultiplier";

        /// <summary> 近战攻速倍率（1.0 = 无加成） </summary>
        public const string MeleeAttackSpeedMultiplier = "Combat_MeleeAttackSpeedMultiplier";
    }
}
