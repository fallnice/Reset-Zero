using UnityEngine;
using Role;
using Enemy.Navigation;

namespace Enemy
{
    /// <summary> 敌人 AI 决策状态 </summary>
    public enum EnemyAIState
    {
        Idle,   // 待机：无目标，原地待命
        Chase,  // 追击：向目标移动
        Attack, // 攻击：面向目标，按冷却触发近战攻击
        Dead    // 死亡：停止一切输出
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

        private EnemyAIState _state = EnemyAIState.Idle;
        private float _nextAttackTime;

        /// <summary> 当前决策状态（供调试/表现层查询） </summary>
        public EnemyAIState CurrentState => _state;
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

            // 死亡优先：角色死亡后停止感知与决策，只输出静止
            if (_character.Health != null && _character.Health.IsDead)
            {
                SetState(EnemyAIState.Dead);
            }
            else
            {
                // HealthController.ResetHealth 后允许池化敌人从 Dead 恢复决策与初始武器。
                if (_state == EnemyAIState.Dead)
                {
                    SetState(EnemyAIState.Idle);
                    TryEquipInitialWeapon();
                }
                _perception.Update(_character, config, _blackboard);
                DecideState();
            }

            ExecuteState();
        }

        /// <summary> 状态迁移：感知结果驱动 Idle/Chase/Attack 之间的切换 </summary>
        private void DecideState()
        {
            switch (_state)
            {
                case EnemyAIState.Idle:
                    if (_blackboard.HasTarget)
                        SetState(EnemyAIState.Chase);
                    break;

                case EnemyAIState.Chase:
                    if (!_blackboard.HasTarget)
                        SetState(EnemyAIState.Idle);
                    else if (_blackboard.DistanceToTarget <= config.attackRange
                        && _navigation.HasReachedDestination)
                        SetState(EnemyAIState.Attack);
                    break;

                case EnemyAIState.Attack:
                    if (!_blackboard.HasTarget)
                        SetState(EnemyAIState.Idle);
                    else if (_blackboard.DistanceToTarget > config.attackRange)
                        SetState(EnemyAIState.Chase);
                    break;
            }
        }

        /// <summary> 执行当前状态：主动驱动导航，再把移动/攻击意图写入 AIInputProvider </summary>
        private void ExecuteState()
        {
            switch (_state)
            {
                case EnemyAIState.Idle:
                    _navigation.Stop();
                    _aiInput.SetMoveDirection(Vector3.zero);
                    _aiInput.SetLookDirection(_character.transform.forward);
                    break;

                case EnemyAIState.Chase:
                    {
                        // 看得见就朝当前位置走；看不见则走向最后已知位置（4.2 感知扩展）
                        Vector3 chaseDestination = _blackboard.HasLineOfSight
                            ? _blackboard.Target.transform.position
                            : _blackboard.LastKnownTargetPosition;
                        _navigation.SetDestination(chaseDestination);
                        if (_character.CanMove)
                        {
                            _navigation.Tick(Time.deltaTime);
                            Vector3 move = _navigation.MoveDirection;
                            _aiInput.SetMoveDirection(move);
                            if (move.sqrMagnitude > 0.0001f)
                                _aiInput.SetLookDirection(move);
                        }
                        else
                        {
                            // 死亡/眩晕等强状态下暂停 Tick，避免静止被误诊为卡住
                            _aiInput.SetMoveDirection(Vector3.zero);
                        }
                    }
                    break;

                case EnemyAIState.Attack:
                    {
                        _navigation.Stop();
                        Vector3 toTarget = _blackboard.Target.transform.position - _character.transform.position;
                        toTarget.y = 0f;
                        if (toTarget.sqrMagnitude > 0.0001f)
                        {
                            toTarget.Normalize();
                            // 近战命中方向取决于角色朝向（attacker.forward），先转向目标再攻击
                            _character.RotateToward(toTarget);
                            _aiInput.SetLookDirection(toTarget);
                        }

                        _aiInput.SetMoveDirection(Vector3.zero);
                        if (Time.time >= _nextAttackTime)
                        {
                            _aiInput.SetAttackPressed(true);
                            _nextAttackTime = Time.time + config.attackCooldown;
                        }
                    }
                    break;

                case EnemyAIState.Dead:
                    _navigation.Stop();
                    _aiInput.SetMoveDirection(Vector3.zero);
                    break;
            }
        }

        private void SetState(EnemyAIState newState)
        {
            if (_state == newState) return;
            _state = newState;
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
        }
    }
}
