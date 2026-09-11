using Role;
using UnityEngine;

namespace Enemy
{
    /// <summary> 当前掩体状态；第四阶段 Utility/战术层据此评分 </summary>
    public enum CoverStatus
    {
        None,       // 无掩体
        Partial,    // 半掩体
        Full        // 全掩体
    }

    /// <summary> 粗略威胁等级；由自身生命、被瞄准情况、周围同伴等汇总 </summary>
    public enum ThreatLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// 敌人 AI 决策黑板——单个敌人的实例级感知/战术数据。
    ///
    /// 这是第四阶段的共享上下文：感知层写入事实（是否看见、最后已知位置、听力线索、掩体、威胁），
    /// 决策层（HFSM/Utility）读取并决定去哪、做什么；两者不互相持有引用。
    ///
    /// 注意：它只描述「敌人知道什么」，不直接驱动移动或攻击；
    /// 移动仍由 EnemyBrain 经 IEnemyNavigation → AIInputProvider → CharacterController 执行。
    /// </summary>
    public class EnemyAIBlackboard
    {
        /// <summary> 当前锁定目标；null 表示无目标 </summary>
        public CharacterRoot Target { get; private set; }

        /// <summary> 是否有锁定目标 </summary>
        public bool HasTarget => Target != null;

        /// <summary> 到目标的距离；无目标时为 0 </summary>
        public float DistanceToTarget { get; private set; }

        /// <summary> 本帧是否能看见目标（距离 + 视野角 + 视线未被遮挡） </summary>
        public bool HasLineOfSight { get; private set; }

        /// <summary> 最后一次确认目标所在位置；Investigate/ReturnHome 依赖它 </summary>
        public Vector3 LastKnownTargetPosition { get; private set; }

        /// <summary> 是否有有效的最后已知位置 </summary>
        public bool HasLastKnownPosition { get; private set; }

        /// <summary> 距离上次看见目标经过的秒数；用于判断是否放弃搜索 </summary>
        public float TimeSinceLastSeen { get; private set; }

        /// <summary> 怀疑度 0~1：听到动静、目标短暂消失后上升，到达 Investigate 阈值 </summary>
        public float Suspicion { get; private set; }

        /// <summary> 听力/受击线索位置（如开枪声、受击方向） </summary>
        public Vector3 LastHeardPosition { get; private set; }

        /// <summary> 是否有待处理的听觉线索 </summary>
        public bool HasHeardClue { get; private set; }

        /// <summary> 自身掩体状态 </summary>
        public CoverStatus Cover { get; private set; }

        /// <summary> 感知到的威胁等级 </summary>
        public ThreatLevel Threat { get; private set; }

        /// <summary> 自身生命比例 0~1；Utility 撤退/激进评分使用 </summary>
        public float HealthRatio { get; private set; } = 1f;

        // ===== 写入接口（由 EnemyPerception 调用）=====

        /// <summary> 每帧开始刷新：推进计时并更新自身状态，不清空感知事实 </summary>
        public void BeginUpdate(CharacterRoot self)
        {
            TimeSinceLastSeen += Time.deltaTime;
            HealthRatio = ResolveHealthRatio(self);
        }

        /// <summary> 设置当前锁定目标与距离 </summary>
        public void SetTarget(CharacterRoot target, float distance)
        {
            Target = target;
            DistanceToTarget = target != null ? distance : 0f;
            if (target == null) HasLineOfSight = false;
        }

        /// <summary> 设置视线结果；看见目标时同时刷新最后已知位置 </summary>
        public void SetLineOfSight(bool hasLineOfSight, Vector3 seenPosition)
        {
            HasLineOfSight = hasLineOfSight;
            if (!hasLineOfSight) return;

            LastKnownTargetPosition = seenPosition;
            HasLastKnownPosition = true;
            TimeSinceLastSeen = 0f;
        }

        /// <summary> 设置怀疑度（0~1） </summary>
        public void SetSuspicion(float suspicion)
        {
            Suspicion = Mathf.Clamp01(suspicion);
        }

        /// <summary> 记录一次听觉/受击线索 </summary>
        public void SetHeardClue(Vector3 position)
        {
            LastHeardPosition = position;
            HasHeardClue = true;
        }

        /// <summary> 听觉线索已被 Investigate 消费 </summary>
        public void ClearHeardClue()
        {
            HasHeardClue = false;
        }

        /// <summary> 目标彻底丢失：清空目标与最后已知位置 </summary>
        public void ClearTarget()
        {
            Target = null;
            DistanceToTarget = 0f;
            HasLineOfSight = false;
            HasLastKnownPosition = false;
            LastKnownTargetPosition = Vector3.zero;
        }

        /// <summary> 设置掩体与威胁评估 </summary>
        public void SetTacticalInfo(CoverStatus cover, ThreatLevel threat)
        {
            Cover = cover;
            Threat = threat;
        }

        private static float ResolveHealthRatio(CharacterRoot self)
        {
            HealthController health = self != null ? self.Health : null;
            if (health == null || health.maxHealth <= 0f) return 1f;
            return Mathf.Clamp01(health.CurrentHealth / health.maxHealth);
        }
    }
}
