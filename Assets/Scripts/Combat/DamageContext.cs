using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 伤害上下文——一次伤害事件的完整信息，取代 TakeDamage(float)，
    /// 为攻击者、命中点、方向、阵营等预留正式边界（未来可扩展伤害类型、暴击、击退等）。
    /// </summary>
    public struct DamageContext
    {
        /// <summary> 伤害数值（扣除前的原始值） </summary>
        public float amount;

        /// <summary> 攻击者（可为 null 表示无来源伤害，如环境伤害） </summary>
        public GameObject attacker;

        /// <summary> 伤害来源阵营（用于友伤/自伤判定） </summary>
        public Faction sourceFaction;

        /// <summary> 命中点（世界空间） </summary>
        public Vector3 hitPoint;

        /// <summary> 伤害方向（世界空间，用于击退/表现） </summary>
        public Vector3 direction;

        /// <summary> 解析伤害来源的阵营；无阵营标识时回退 Neutral </summary>
        public static Faction ResolveFaction(Transform source)
        {
            if (source == null) return Faction.Neutral;
            IFactionMember member = source.GetComponentInParent<IFactionMember>();
            return member != null ? member.Faction : Faction.Neutral;
        }
    }
}
