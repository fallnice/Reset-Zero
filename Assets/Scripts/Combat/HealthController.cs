using System;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 通用生命组件——实现 IDamageable，管理当前/最大生命、受伤与死亡。
    /// 自身阵营从父级 CharacterRoot（IFactionMember）解析，用于友伤过滤；
    /// 无阵营身份（如训练假人）时回退 Neutral，不参与友伤判定。
    /// 死亡不直接销毁对象，由 Died 事件交给上层（角色协调器/Brain/表现层）决策。
    /// </summary>
    public class HealthController : MonoBehaviour, IDamageable
    {
        [Header("生命")]
        [Min(0f)] public float maxHealth = 100f;

        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary> 受击回调：参数为剩余血量 </summary>
        public event Action<float> Damaged;

        /// <summary> 受击回调（带完整伤害上下文） </summary>
        public event Action<DamageContext> DamagedWithContext;

        /// <summary> 死亡回调（血量首次归零触发一次） </summary>
        public event Action Died;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(DamageContext context)
        {
            if (IsDead || context.amount <= 0f) return;

            // 同阵营友伤保护：Neutral 不参与阵营判定，其余阵营互斥
            Faction myFaction = ResolveSelfFaction();
            if (myFaction != Faction.Neutral && context.sourceFaction == myFaction) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - context.amount);
            Damaged?.Invoke(CurrentHealth);
            DamagedWithContext?.Invoke(context);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
            }
        }

        /// <summary> 解析自身阵营：优先取父级 CharacterRoot 的 IFactionMember，无身份时回退 Neutral </summary>
        private Faction ResolveSelfFaction()
        {
            IFactionMember member = GetComponentInParent<IFactionMember>();
            return member != null ? member.Faction : Faction.Neutral;
        }

        /// <summary> 重置生命（复活/新一轮测试） </summary>
        public void ResetHealth()
        {
            IsDead = false;
            CurrentHealth = maxHealth;
        }
    }
}
