namespace Combat
{
    /// <summary>
    /// 角色战斗属性——实例级容器，替代静态 Blackboard 中的攻击/攻速加成。
    /// 玩家由 BonusController 累加；敌人持有自己的默认值（1.0），不继承玩家加成。
    /// </summary>
    public class CombatStats
    {
        /// <summary> 攻击力倍率（1.0 = 无加成，1.15 = +15%） </summary>
        public float attackMultiplier = 1f;

        /// <summary> 近战攻速倍率（1.0 = 无加成） </summary>
        public float meleeAttackSpeedMultiplier = 1f;
    }
}
