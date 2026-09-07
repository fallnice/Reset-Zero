using System;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 训练假人——实现 IDamageable 的最简可命中目标，用于验证近战/枪械命中链路。
    /// 挂在假人根对象上；无 Collider 时自动补一个 SphereCollider 以便被命中。
    /// 表现反馈通过 Damaged/Died 实例事件订阅，不依赖具体 UI/特效。
    /// </summary>
    public class DummyTarget : MonoBehaviour, IDamageable
    {
        [Header("生命")]
        [Min(0f)] public float maxHealth = 100f;

        [Header("死亡表现")]
        [Tooltip("血量归零后是否销毁自身；否则仅停止受击（可 ResetHealth 复活）")]
        public bool destroyOnDeath = false;

        /// <summary> 受击回调：参数为剩余血量 </summary>
        public event Action<float> Damaged;

        /// <summary> 死亡回调（血量首次归零触发一次） </summary>
        public event Action Died;

        /// <summary> 当前血量 </summary>
        public float CurrentHealth { get; private set; }

        /// <summary> 是否已死亡（血量归零后不再受击） </summary>
        public bool IsDead { get; private set; }

        private void Awake()
        {
            CurrentHealth = maxHealth;
            EnsureCollider();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            Damaged?.Invoke(CurrentHealth);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
                if (destroyOnDeath) Destroy(gameObject);
            }
        }

        /// <summary> 重置血量（复活/新一轮测试） </summary>
        public void ResetHealth()
        {
            IsDead = false;
            CurrentHealth = maxHealth;
        }

        /// <summary> 没有碰撞体就无法被 OverlapSphere / Raycast 命中，自动补一个保证开箱可用 </summary>
        private void EnsureCollider()
        {
            if (GetComponent<Collider>() == null)
                gameObject.AddComponent<SphereCollider>();
        }
    }
}
