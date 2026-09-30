using Combat;
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
        /// <summary> 听觉线索的绝对失效时间。 </summary>
        public float HeardClueExpiresAt { get; private set; }
        /// <summary> 调查态是否已锁定一个事实锚点；锁定后不受原始线索过期影响。 </summary>
        public bool HasCommittedInvestigation { get; private set; }

        /// <summary> 同阵营单位最近一次共享的目标确认位置。 </summary>
        public Vector3 SharedAlertPosition { get; private set; }
        /// <summary> 是否保存了一条尚未消费的共享报警。 </summary>
        public bool HasSharedAlert { get; private set; }
        /// <summary> 共享报警的绝对失效时间。 </summary>
        public float SharedAlertExpiresAt { get; private set; }
        /// <summary> 最近接受的报警序列，用于拒绝重复或乱序投递。 </summary>
        public long LastSharedAlertSequence { get; private set; }

        /// <summary> 自身掩体状态 </summary>
        public CoverStatus Cover { get; private set; }

        /// <summary> 感知到的威胁等级 </summary>
        public ThreatLevel Threat { get; private set; }

        /// <summary> 自身生命比例 0~1；Utility 撤退/激进评分使用 </summary>
        public float HealthRatio { get; private set; } = 1f;

        // ===== 战术决策（4.3 Utility，由 EnemyUtilityEvaluator 写入）=====

        /// <summary> 当前战术选择；无目标时为 Engage（等价于原追击行为） </summary>
        public EnemyTacticalChoice TacticalChoice { get; private set; } = EnemyTacticalChoice.Engage;

        /// <summary> 当前战术还要保持的秒数；防止评分抖动导致每帧换战术 </summary>
        public float TacticalCommitRemaining { get; private set; }

        /// <summary> 撤退目标点：远离当前目标且尽量留在防区范围内 </summary>
        public Vector3 RetreatPoint { get; private set; }

        /// <summary> 包抄目标点：绕到目标侧翼 </summary>
        public Vector3 FlankPoint { get; private set; }

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

        /// <summary>
        /// 记录一次听觉/受击线索。
        ///
        /// TODO(4.2 待办)：生产者是 `EnemyHearing`——它订阅 HealthController.DamagedWithContext（受击）
        /// 与 EquipmentController.AttackCommitted（枪响近似），超出 `hearingRange` 的会被忽略。
        /// 目前还缺一个带攻击者位置的「武器开火」事件，所以枪响只能用攻击者当前位置近似，
        /// 且无法区分近战与消音武器；等战斗层补 `Weapon_Fired(attacker, position)` 后替换。
        /// </summary>
        public void SetHeardClue(Vector3 position, float lifetimeSeconds)
        {
            LastHeardPosition = position;
            HeardClueExpiresAt = Time.time + Mathf.Max(0.1f, lifetimeSeconds);
            HasHeardClue = true;
        }

        /// <summary> 检查听觉线索是否仍有效；过期时同步清理位置和标记。 </summary>
        public bool HasValidHeardClue(float now)
        {
            if (!HasHeardClue) return false;
            if (now < HeardClueExpiresAt) return true;

            ClearHeardClue();
            return false;
        }

        /// <summary> 在现有怀疑度上追加一份（听到动静时使用），结果夹紧到 0~1 </summary>
        public void AddSuspicion(float amount)
        {
            Suspicion = Mathf.Clamp01(Suspicion + Mathf.Max(0f, amount));
        }

        /// <summary> 听觉线索已被 Investigate 消费 </summary>
        public void ClearHeardClue()
        {
            HasHeardClue = false;
            HeardClueExpiresAt = 0f;
            LastHeardPosition = Vector3.zero;
        }

        /// <summary> 标记调查态已经锁定事实锚点，感知层应维持怀疑度直到状态退出。 </summary>
        public void BeginCommittedInvestigation()
        {
            HasCommittedInvestigation = true;
        }

        /// <summary> 调查结束或被更高优先级状态打断时释放怀疑度维持标记。 </summary>
        public void EndCommittedInvestigation()
        {
            HasCommittedInvestigation = false;
        }

        /// <summary> 接受一条更新的共享报警，并把怀疑度提升到可调查水平。 </summary>
        public bool TrySetSharedAlert(in EnemyAlert alert, float investigateThreshold)
        {
            if (alert.Sequence <= LastSharedAlertSequence || Time.time >= alert.ExpiresAt) return false;

            SharedAlertPosition = alert.Position;
            SharedAlertExpiresAt = alert.ExpiresAt;
            LastSharedAlertSequence = alert.Sequence;
            HasSharedAlert = true;
            Suspicion = Mathf.Max(Suspicion, Mathf.Clamp01(Mathf.Max(
                investigateThreshold, alert.SuspicionBoost)));
            return true;
        }

        /// <summary> 检查共享报警是否仍有效；过期时立即清理事实。 </summary>
        public bool HasValidSharedAlert(float now)
        {
            if (!HasSharedAlert) return false;
            if (now < SharedAlertExpiresAt) return true;

            ClearSharedAlert();
            return false;
        }

        /// <summary> 消费或丢弃共享报警，但保留已接受序列以拒绝迟到重复包。 </summary>
        public void ClearSharedAlert()
        {
            HasSharedAlert = false;
            SharedAlertPosition = Vector3.zero;
            SharedAlertExpiresAt = 0f;
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

        /// <summary>
        /// 复活/池化复用时清空全部感知事实，避免带着上一轮的目标与怀疑度进入新生命周期。
        /// 调查结束、彻底失去线索时也调用它，否则黑板会一直认为「还记得位置」。
        /// </summary>
        public void Reset()
        {
            ClearTarget();
            ClearHeardClue();
            LastHeardPosition = Vector3.zero;
            ClearSharedAlert();
            LastSharedAlertSequence = 0;
            EndCommittedInvestigation();
            Suspicion = 0f;
            TimeSinceLastSeen = 0f;
            Cover = CoverStatus.None;
            Threat = ThreatLevel.Low;
            HealthRatio = 1f;
            TacticalChoice = EnemyTacticalChoice.Engage;
            TacticalCommitRemaining = 0f;
            RetreatPoint = Vector3.zero;
            FlankPoint = Vector3.zero;
        }

        /// <summary> 设置掩体与威胁评估 </summary>
        public void SetTacticalInfo(CoverStatus cover, ThreatLevel threat)
        {
            Cover = cover;
            Threat = threat;
        }

        /// <summary>
        /// 写入战术决策：选择、保持时长与对应目标点。
        /// 保持时长只用于抑制抖动；遇到更紧急的情况（残血、目标丢失）时会被 ForceTactical 覆盖。
        /// </summary>
        public void SetTacticalDecision(
            EnemyTacticalChoice choice,
            float commitSeconds,
            Vector3 retreatPoint,
            Vector3 flankPoint)
        {
            TacticalChoice = choice;
            TacticalCommitRemaining = Mathf.Max(0f, commitSeconds);
            RetreatPoint = retreatPoint;
            FlankPoint = flankPoint;
        }

        /// <summary>
        /// 强制切换到更紧急的战术：清掉原战术的保持时长，并清空不再使用的战术点，
        /// 避免切换到 Engage 之后还残留上一轮的撤退点/包抄点。
        /// </summary>
        public void ForceTactical(EnemyTacticalChoice choice, float commitSeconds)
        {
            TacticalChoice = choice;
            TacticalCommitRemaining = Mathf.Max(0f, commitSeconds);
            if (choice != EnemyTacticalChoice.Retreat) RetreatPoint = Vector3.zero;
            if (choice != EnemyTacticalChoice.Flank) FlankPoint = Vector3.zero;
        }

        /// <summary> 递减当前战术的保持时长 </summary>
        public void TickTacticalCommit(float deltaTime)
        {
            if (TacticalCommitRemaining > 0f)
                TacticalCommitRemaining = Mathf.Max(0f, TacticalCommitRemaining - deltaTime);
        }

        /// <summary> 当前战术已经保持了多久（0 表示刚开始或没有保持时长） </summary>
        public float GetTacticalElapsed(float commitSeconds)
        {
            return Mathf.Max(0f, commitSeconds - TacticalCommitRemaining);
        }

        private static float ResolveHealthRatio(CharacterRoot self)
        {
            HealthController health = self != null ? self.Health : null;
            if (health == null || health.maxHealth <= 0f) return 1f;
            return Mathf.Clamp01(health.CurrentHealth / health.maxHealth);
        }
    }
}
