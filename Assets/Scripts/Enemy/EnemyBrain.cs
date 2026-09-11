using UnityEngine;
using Role;
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

        /// <summary> 决策黑板（供调试/表现层与后续 Utility 读取） </summary>
        public EnemyAIBlackboard Blackboard => _blackboard;

        private readonly EnemyAIContext _aiContext = new EnemyAIContext();
        private IEnemyState _currentState;

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

        private void Awake()
        {
            _character = GetComponent<CharacterRoot>();
            _aiInput = GetComponent<AIInputProvider>();
            ResolveNavigation();
            _navigation?.Initialize(transform, GetComponent<CharacterController>(), config);

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

        private void Start()
        {
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
            if (_character == null || _aiInput == null || config == null) return;

            // 重置本帧攻击意图：由决策循环统一管理边缘触发，避免 LateUpdate 清除的时序竞态
            _aiInput.SetAttackPressed(false);
            _aiContext.BeginFrame();

            // 每帧先刷新上下文，保证任何 OnEnter/OnUpdate 都读到本帧依赖
            _aiContext.Character = _character;
            _aiContext.AiInput = _aiInput;
            _aiContext.Navigation = _navigation;
            _aiContext.Blackboard = _blackboard;
            _aiContext.Config = config;
            _aiContext.DeltaTime = Time.deltaTime;
            if (_aiContext.Grid == null)
            {
                // 与项目其它模块保持一致仍用 FindObjectOfType；无网格时 Grid 恒为 null，Patrol 会自动退回 Idle。
                _aiContext.Grid = Object.FindObjectOfType<EnemyNavigationGrid>();
            }

            // 强状态优先：死亡 > 眩晕 > 常规决策
            if (_character.Health != null && _character.Health.IsDead)
            {
                TransitionTo(EnemyAIState.Dead);
            }
            else if (IsStunned())
            {
                TransitionTo(EnemyAIState.Stunned);
            }
            else
            {
                // HealthController.ResetHealth 后允许池化敌人从 Dead 恢复决策、初始武器与感知事实。
                if (_currentState != null && _currentState.Kind == EnemyAIState.Dead)
                {
                    // 池化复用可能换个出生点，重设归位基准，避免以旧出生点判定「离家过远」
                    _aiContext.HomePosition = transform.position;
                    _aiContext.ResetTimers();
                    _blackboard.Reset();
                    _perception.Reset();
                    TryEquipInitialWeapon();
                }

                _perception.Update(_character, config, _blackboard);
                TransitionTo(GetDesiredState());
            }

            _aiContext.UpdateNavigationFailure(Time.deltaTime);
            _currentState?.OnUpdate(_aiContext);

            if (_aiContext.HasPendingTransition)
                TransitionTo(_aiContext.PendingTransition);

            if (config.debugDrawState && Time.frameCount % 30 == 0)
                Debug.Log($"[EnemyBrain] {name} 状态={CurrentState} 导航={NavigationStatus}", this);
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
            if (config == null || _character == null) return;

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
