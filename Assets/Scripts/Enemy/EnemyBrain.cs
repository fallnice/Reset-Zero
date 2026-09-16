using UnityEngine;
using Role;
using Role.Controllers;
using Role.Core;
using Enemy.Navigation;
using Enemy.States;

namespace Enemy
{
    /// <summary> 敌人 AI 决策状态 </summary>
    public enum EnemyAIState
    {
        Idle,        // 待机：原地警戒
        Patrol,      // 巡逻：出生点周围随机走动
        Chase,       // 追击：向目标（或最后已知位置）移动
        Attack,      // 攻击：面向目标，按冷却触发近战攻击
        Investigate, // 调查：前往最后已知位置/听觉线索并搜索
        ReturnHome,  // 归位：返回出生点，避免被拉离防区
        Stunned,     // 眩晕：强状态，暂停一切决策
        Dead         // 死亡：终态，停止一切输出
    }

    /// <summary>
    /// 敌人 AI 大脑——感知 → 决策 → 输出到 AIInputProvider。
    /// 导航只产生方向，最终仍由 CharacterRoot → CharacterController.Move() 执行位移。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterRoot))]
    [RequireComponent(typeof(AIInputProvider))]
    [RequireComponent(typeof(EnemyHearing))]
    public class EnemyBrain : MonoBehaviour
    {
        [SerializeField] private EnemyConfig config;
        [Tooltip("可选：拖入同物体的 GridAStarNavigation；为空时自动查找，仍无则回退 DirectNavigation")]
        [SerializeField] private MonoBehaviour navigationSource;

        private CharacterRoot _character;
        private AIInputProvider _aiInput;
        private readonly EnemyPerception _perception = new EnemyPerception();
        private readonly EnemyAIBlackboard _blackboard = new EnemyAIBlackboard();
        private IEnemyNavigation _navigation;
        private ILocalAvoidance _localAvoidance;

        /// <summary> 决策黑板（供调试/表现层与后续 Utility 读取） </summary>
        public EnemyAIBlackboard Blackboard => _blackboard;

        private readonly EnemyAIContext _aiContext = new EnemyAIContext();
        private EnemyHearing _hearing;
        private IEnemyState _currentState;
        private bool _runtimeEventsSubscribed;
        private bool _wasDead;
        private bool _hasStarted;

        private readonly IdleState _idleState = new IdleState();
        private readonly PatrolState _patrolState = new PatrolState();
        private readonly ChaseState _chaseState = new ChaseState();
        private readonly AttackState _attackState = new AttackState();
        private readonly InvestigateState _investigateState = new InvestigateState();
        private readonly ReturnHomeState _returnHomeState = new ReturnHomeState();
        private readonly StunnedState _stunnedState = new StunnedState();
        private readonly DeadState _deadState = new DeadState();

        /// <summary> 当前决策状态（供调试/表现层查询） </summary>
        public EnemyAIState CurrentState => _currentState != null ? _currentState.Kind : EnemyAIState.Idle;
        /// <summary> 当前导航状态（供调试/场景诊断查询） </summary>
        public EnemyNavigationStatus NavigationStatus => _navigation != null
            ? _navigation.Status
            : EnemyNavigationStatus.Failed;
        /// <summary> 最近一次导航失败原因。 </summary>
        public EnemyNavigationFailure NavigationFailure => _navigation != null
            ? _navigation.LastFailure
            : EnemyNavigationFailure.GridUnavailable;
        /// <summary> 局部避障状态。 </summary>
        public LocalAvoidanceStatus AvoidanceStatus => _localAvoidance != null
            ? _localAvoidance.Status
            : LocalAvoidanceStatus.Disabled;
        /// <summary> 局部避障最近一次识别到的邻居数。 </summary>
        public int AvoidanceNeighborCount => _localAvoidance != null ? _localAvoidance.NeighborCount : 0;
        /// <summary> 邻居查询缓冲区是否在最近一次采样中被填满。 </summary>
        public bool IsAvoidanceNeighborBufferSaturated =>
            _localAvoidance != null && _localAvoidance.IsNeighborBufferSaturated;
        /// <summary> 寻路层给出的原始方向。 </summary>
        public Vector3 RawNavigationDirection => _localAvoidance != null
            ? _localAvoidance.RawDirection
            : (_navigation != null ? _navigation.MoveDirection : Vector3.zero);
        /// <summary> 写入 AIInputProvider 前的最终修正方向。 </summary>
        public Vector3 AdjustedNavigationDirection => _localAvoidance != null
            ? _localAvoidance.AdjustedDirection
            : (_navigation != null ? _navigation.MoveDirection : Vector3.zero);

        private void Awake()
        {
            _character = GetComponent<CharacterRoot>();
            _aiInput = GetComponent<AIInputProvider>();
            // 注册到角色上，EnemyRegistry 才能把武器声广播给本敌人
            if (_character != null)
                _character.SetBrain(this);
            CharacterController controller = GetComponent<CharacterController>();
            ResolveNavigation();
            _navigation?.Initialize(transform, controller, config);
            // 避障保持为纯 C# 策略对象，不增加 Prefab 组件负担；与导航共用角色和控制器依赖。
            _localAvoidance = new ContextSteeringAvoidance();
            _localAvoidance.Initialize(transform, controller, config);

            // 听觉：受击自己订阅，武器声由下面的 AttackCommitted 转发给它
            _hearing = GetComponent<EnemyHearing>();
            _hearing?.Initialize(_blackboard, _character != null ? _character.Health : null, config);

            // Utility 评分器：无配置时保持 Engage，不影响原有追击行为
            _aiContext.Tactical = config != null ? new EnemyUtilityEvaluator(config) : null;

            // 出生点用于 Patrol 取点与 ReturnHome 判定
            _aiContext.HomePosition = transform.position;
        }

        /// <summary> 显式引用优先，其次自动查找接口组件；旧 Prefab 无 A* 组件时兼容回退直线导航 </summary>
        private void ResolveNavigation()
        {
            if (navigationSource != null)
            {
                _navigation = navigationSource as IEnemyNavigation;
                if (_navigation == null)
                    Debug.LogError("[EnemyBrain] Navigation Source 未实现 IEnemyNavigation，将回退 DirectNavigation", this);
            }

            if (_navigation == null)
            {
                MonoBehaviour[] components = GetComponents<MonoBehaviour>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is IEnemyNavigation candidate)
                    {
                        _navigation = candidate;
                        break;
                    }
                }
            }

            if (_navigation == null)
                _navigation = new DirectNavigation(config);
        }

        private void OnEnable()
        {
            SubscribeRuntimeEvents();
            // 首次启用后 Start 会完成初始化；仅对后续重新启用执行池化状态清理。
            if (_hasStarted)
                ResetAfterEnable();
        }

        private void OnDisable()
        {
            UnsubscribeRuntimeEvents();
            ClearInputAndNavigation();
        }

        private void OnDestroy()
        {
            UnsubscribeRuntimeEvents();
        }

        private void Start()
        {
            // EnemyBrain 执行顺序(-50)早于 CharacterRoot，Awake 时 HealthController 可能还没解析出来。
            // Start 时所有 Awake 已完成，这里补订一次，避免听觉与生命事件静默失效。
            if (_character != null)
            {
                _hearing?.Initialize(_blackboard, _character.Health, config);
                _hearing?.TrySubscribe();
            }
            SubscribeRuntimeEvents();
            TryEquipInitialWeapon();
            _hasStarted = true;
        }

        /// <summary> 池化或临时禁用后恢复到干净的待机决策状态。 </summary>
        private void ResetAfterEnable()
        {
            // 先清输入与导航，再退出旧状态，避免旧状态阶段数据在恢复首帧继续驱动角色。
            ClearInputAndNavigation();
            _aiContext.ResetTimers();
            _blackboard.Reset();
            _perception.Reset();
            _currentState?.OnExit(_aiContext);
            _currentState = null;
            TryEquipInitialWeapon();
        }

        private void TryEquipInitialWeapon()
        {
            // 开局/复活装备初始近战武器，让攻击链路复用 EquipmentController；已有武器时不覆盖。
            if (_character == null || config == null || (_character.Health != null && _character.Health.IsDead)) return;
            if (_character.Equipment == null)
            {
                Debug.LogWarning("[EnemyBrain] 缺少 EquipmentController，敌人无法攻击", this);
                return;
            }
            if (_character.Equipment.CurrentWeapon != null) return;
            if (config.meleeWeapon == null)
            {
                Debug.LogWarning("[EnemyBrain] EnemyConfig 未配置 Melee Weapon，敌人无法攻击", this);
                return;
            }
            if (!_character.Equipment.Pickup(config.meleeWeapon))
                Debug.LogWarning("[EnemyBrain] 初始武器装备失败，敌人无法攻击", this);
        }

        private void Update()
        {
            if (config == null)
            {
                Debug.LogWarning("[EnemyBrain] 未分配 EnemyConfig，敌人 AI 已停止", this);
                ClearInputAndNavigation();
                enabled = false;
                return;
            }
            if (_character == null || _aiInput == null) return;

            // 重置本帧攻击意图：由决策循环统一管理边缘触发，避免 LateUpdate 清除的时序竞态
            _aiInput.SetAttackPressed(false);
            _aiContext.BeginFrame();

            // 每帧先刷新上下文，保证任何 OnEnter/OnUpdate 都读到本帧依赖
            _aiContext.Character = _character;
            _aiContext.AiInput = _aiInput;
            _aiContext.Navigation = _navigation;
            _aiContext.LocalAvoidance = _localAvoidance;
            _aiContext.Blackboard = _blackboard;
            _aiContext.Config = config;
            _aiContext.DeltaTime = Time.deltaTime;
            _aiContext.TickCooldowns(Time.deltaTime);
            _blackboard.TickTacticalCommit(Time.deltaTime);
            if (_aiContext.Grid == null)
            {
                // 与项目其它模块保持一致仍用 FindObjectOfType；无网格时 Grid 恒为 null，Patrol 会自动退回 Idle。
                _aiContext.Grid = Object.FindObjectOfType<EnemyNavigationGrid>();
            }

            bool isDead = _character.Health != null && _character.Health.IsDead;

            // 强状态优先：死亡 > 眩晕 > 常规决策
            if (isDead)
            {
                _wasDead = true;
                TransitionTo(EnemyAIState.Dead);
            }
            else if (IsStunned())
            {
                TransitionTo(EnemyAIState.Stunned);
            }
            else
            {
                // 上一轮死亡、现在已复活（HealthController.ResetHealth）：一次性恢复决策与装备
                if (_wasDead)
                    RecoverFromDeath();

                _perception.Update(_character, config, _blackboard);

                // 战术计时：撤退与包抄的超时兜底依赖它，必须在状态执行前刷新
                _aiContext.TacticalElapsed = _blackboard.GetTacticalElapsed(config.tacticalCommitSeconds);
                _aiContext.RetreatElapsed = _blackboard.TacticalChoice == EnemyTacticalChoice.Retreat
                    ? _aiContext.RetreatElapsed + Time.deltaTime
                    : 0f;
                TransitionTo(GetDesiredState());
            }

            _aiContext.UpdateNavigationFailure(Time.deltaTime);
            _currentState?.OnUpdate(_aiContext);

            if (_aiContext.HasPendingTransition)
                TransitionTo(_aiContext.PendingTransition);
        }

        /// <summary> 安全停止导航、避障、移动和攻击输出。 </summary>
        private void ClearInputAndNavigation()
        {
            _navigation?.Stop();
            _localAvoidance?.Reset();
            if (_aiInput == null) return;

            _aiInput.SetMoveDirection(Vector3.zero);
            _aiInput.SetAttackPressed(false);
        }

        private bool IsStunned()
        {
            CharacterStateCoordinator coordinator = _character != null ? _character.Coordinator : null;
            return coordinator != null && coordinator.CurrentState == CharacterState.Stunned;
        }

        /// <summary> 常规决策下的目标状态：由黑板中的目标与怀疑度决定 </summary>
        private EnemyAIState GetDesiredState()
        {
            if (_currentState == null) return EnemyAIState.Idle;

            switch (_currentState.Kind)
            {
                case EnemyAIState.Dead:
                    // 复活后回到待机，再由 Idle 自行进入巡逻
                    return EnemyAIState.Idle;

                case EnemyAIState.Stunned:
                    return EnemyAIState.Idle;

                case EnemyAIState.Idle:
                    if (_blackboard.HasTarget) return EnemyAIState.Chase;
                    if (_blackboard.Suspicion >= config.investigateSuspicionThreshold)
                        return EnemyAIState.Investigate;
                    return EnemyAIState.Idle;

                case EnemyAIState.Patrol:
                    if (_blackboard.HasTarget) return EnemyAIState.Chase;
                    if (_blackboard.Suspicion >= config.investigateSuspicionThreshold)
                        return EnemyAIState.Investigate;
                    return EnemyAIState.Patrol;

                case EnemyAIState.Chase:
                    // 即使还有目标，离家过远也要先归位（与 ChaseState 内部判断保持一致）
                    if (_aiContext.IsFarFromHome()) return EnemyAIState.ReturnHome;
                    if (_blackboard.HasTarget) return EnemyAIState.Chase;
                    return _blackboard.HasLastKnownPosition
                        ? EnemyAIState.Investigate
                        : EnemyAIState.ReturnHome;

                case EnemyAIState.Attack:
                    if (_blackboard.HasTarget) return EnemyAIState.Attack;
                    return _blackboard.HasLastKnownPosition
                        ? EnemyAIState.Investigate
                        : EnemyAIState.ReturnHome;

                case EnemyAIState.Investigate:
                    if (_blackboard.HasTarget) return EnemyAIState.Chase;
                    return EnemyAIState.Investigate;

                case EnemyAIState.ReturnHome:
                    // 归位途中重新发现目标时先判断是否已经回到防区：
                    // 没回到防区就继续归位，避免与 Chase 的「离家过远」判定互顶。
                    if (_blackboard.HasTarget && !_aiContext.IsFarFromHome())
                        return EnemyAIState.Chase;
                    return EnemyAIState.ReturnHome;

                default:
                    return EnemyAIState.Idle;
            }
        }

        private IEnemyState ResolveState(EnemyAIState kind)
        {
            switch (kind)
            {
                case EnemyAIState.Idle: return _idleState;
                case EnemyAIState.Patrol: return _patrolState;
                case EnemyAIState.Chase: return _chaseState;
                case EnemyAIState.Attack: return _attackState;
                case EnemyAIState.Investigate: return _investigateState;
                case EnemyAIState.ReturnHome: return _returnHomeState;
                case EnemyAIState.Stunned: return _stunnedState;
                case EnemyAIState.Dead: return _deadState;
                default: return _idleState;
            }
        }

        private void SubscribeRuntimeEvents()
        {
            if (_runtimeEventsSubscribed) return;

            // 依赖未就绪时不置位，留给 Start 或后续帧重试；CharacterRoot.Awake 晚于本组件的 Awake/OnEnable。
            if (_character == null) return;

            EquipmentController equipment = _character.Equipment;
            Combat.HealthController health = _character.Health;
            if (equipment == null || health == null) return;

            equipment.AttackCommitted += HandleAnyAttackCommitted;
            health.Died += HandleHealthDied;
            health.HealthReset += HandleHealthReset;

            _runtimeEventsSubscribed = true;
        }

        private void UnsubscribeRuntimeEvents()
        {
            if (!_runtimeEventsSubscribed) return;

            EquipmentController equipment = _character != null ? _character.Equipment : null;
            if (equipment != null)
                equipment.AttackCommitted -= HandleAnyAttackCommitted;

            Combat.HealthController health = _character != null ? _character.Health : null;
            if (health != null)
            {
                health.Died -= HandleHealthDied;
                health.HealthReset -= HandleHealthReset;
            }

            _runtimeEventsSubscribed = false;
        }

        private void HandleHealthDied()
        {
            _wasDead = true;
        }

        /// <summary> 生命重置：重新绑定听觉依赖，并标记可以回到常规决策 </summary>
        private void HandleHealthReset()
        {
            if (_hearing != null && _character != null)
                _hearing.Initialize(_blackboard, _character.Health, config);
        }

        /// <summary>
        /// 本角色提交攻击：把攻击者位置作为声源广播给所有敌人。
        /// 每个敌人的 EnemyHearing 会自己按距离过滤，并忽略自己的声音。
        /// 全局 EventBus 不带位置，无法优雅订阅，只能由各自 Brain 转发。
        /// </summary>
        private void HandleAnyAttackCommitted(Combat.WeaponConfig weapon)
        {
            if (_character == null) return;
            EnemyRegistry.BroadcastWeaponNoise(_character.transform.position);
        }

        /// <summary> 供 EnemyRegistry 广播调用：让本敌人的听觉处理一次外部武器声 </summary>
        public void NotifyHeardWeaponNoise(Vector3 sourcePosition)
        {
            _hearing?.NotifyWeaponNoise(sourcePosition, transform.position);
        }

        /// <summary>
        /// 从死亡恢复：重设出生点、清空计时与感知事实，并补装初始武器。
        /// 只认 HealthController.ResetHealth（走 HealthReset 事件），避免依赖状态比较的时序。
        /// </summary>
        private void RecoverFromDeath()
        {
            _wasDead = false;
            _aiContext.HomePosition = transform.position;
            _aiContext.ResetTimers();
            _blackboard.Reset();
            _perception.Reset();
            TryEquipInitialWeapon();
        }

        private void TransitionTo(EnemyAIState kind, bool force = false)
        {
            IEnemyState next = ResolveState(kind);
            if (next == null) return;

            if (_currentState == next)
            {
                if (force) next.OnEnter(_aiContext);
                return;
            }

            _currentState?.OnExit(_aiContext);
            _currentState = next;
            next.OnEnter(_aiContext);
        }

        /// <summary> 决策调试可视化：视野锥、当前目标、最后已知位置与视线 </summary>
        private void OnDrawGizmosSelected()
        {
            if (config == null || _character == null || !config.debugDrawState) return;

            Vector3 origin = transform.position;
            Vector3 eye = origin + Vector3.up * config.eyeHeight;

            // 视野锥：以角色朝向为中轴，画视野角两侧边界
            Color coneColor = _blackboard.HasLineOfSight ? Color.red : Color.yellow;
            Gizmos.color = coneColor;
            float half = config.viewAngle * 0.5f;
            Vector3 forward = _character.transform.forward;
            Vector3 leftBoundary = Quaternion.Euler(0f, -half, 0f) * forward;
            Vector3 rightBoundary = Quaternion.Euler(0f, half, 0f) * forward;
            Gizmos.DrawRay(eye, leftBoundary * config.detectionRange);
            Gizmos.DrawRay(eye, rightBoundary * config.detectionRange);
            Gizmos.DrawWireSphere(eye, 0.05f);

            // 最后已知位置：黄框；有目标但无视线时说明在靠记忆追击
            if (_blackboard.HasLastKnownPosition)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(_blackboard.LastKnownTargetPosition + Vector3.up * 0.1f, Vector3.one * 0.4f);
                Gizmos.DrawLine(eye, _blackboard.LastKnownTargetPosition);
            }

            // 当前目标：有视线画绿线，无视线画灰线
            if (_blackboard.HasTarget)
            {
                Gizmos.color = _blackboard.HasLineOfSight ? Color.green : Color.gray;
                Gizmos.DrawLine(eye, _blackboard.Target.transform.position);
            }

            // 导航与局部避障：青色为原始方向，蓝色为最终方向，白圈为邻居查询范围。
            if (_localAvoidance != null)
            {
                Gizmos.color = Color.white;
                DrawHorizontalRing(origin, config.avoidanceNeighborRadius);
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(origin + Vector3.up * 0.15f, RawNavigationDirection * 1.2f);
                Gizmos.color = AvoidanceStatus == LocalAvoidanceStatus.Blocked
                    || IsAvoidanceNeighborBufferSaturated ? Color.red : Color.blue;
                Gizmos.DrawRay(origin + Vector3.up * 0.22f, AdjustedNavigationDirection * 1.2f);
            }

            // 出生点与巡逻范围
            Gizmos.color = Color.cyan;
            DrawHorizontalRing(_aiContext.HomePosition, config.patrolRadius);

            // 巡逻目标点
            if (CurrentState == EnemyAIState.Patrol)
            {
                Gizmos.color = Color.blue;
                Vector3 patrolTarget = _patrolState.PatrolTarget;
                Gizmos.DrawWireSphere(patrolTarget + Vector3.up * 0.1f, 0.3f);
                Gizmos.DrawLine(eye, patrolTarget);
            }

            // 战术点：撤退红、包抄品红、对峙黄圈
            if (CurrentState == EnemyAIState.Chase)
            {
                switch (_blackboard.TacticalChoice)
                {
                    case EnemyTacticalChoice.Retreat:
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(_blackboard.RetreatPoint + Vector3.up * 0.1f, 0.35f);
                        Gizmos.DrawLine(eye, _blackboard.RetreatPoint);
                        break;

                    case EnemyTacticalChoice.Flank:
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawWireSphere(_blackboard.FlankPoint + Vector3.up * 0.1f, 0.35f);
                        Gizmos.DrawLine(eye, _blackboard.FlankPoint);
                        break;

                    case EnemyTacticalChoice.Hold:
                        Gizmos.color = Color.yellow;
                        DrawHorizontalRing(eye, config.attackRange * config.holdDistanceFactor);
                        break;
                }
            }
        }

        private static void DrawHorizontalRing(Vector3 center, float radius)
        {
            if (radius <= 0f) return;

            const int segments = 32;
            float step = Mathf.PI * 2f / segments;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
