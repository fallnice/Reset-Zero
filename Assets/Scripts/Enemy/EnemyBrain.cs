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
        private IEnemyNavigation _navigation;

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
            // 开局装备初始近战武器，让攻击链路可复用（equipmentCtrl 已在 CharacterRoot.Awake 完成 Init）
            if (_character == null || config == null) return;
            if (_character.Equipment == null)
            {
                Debug.LogWarning("[EnemyBrain] 缺少 EquipmentController，敌人无法攻击", this);
                return;
            }
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
            else if (_state != EnemyAIState.Dead)
            {
                _perception.Update(_character, config);
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
                    if (_perception.HasTarget)
                        SetState(EnemyAIState.Chase);
                    break;

                case EnemyAIState.Chase:
                    if (!_perception.HasTarget)
                        SetState(EnemyAIState.Idle);
                    else if (_perception.DistanceToTarget <= config.attackRange
                        && _navigation.HasReachedDestination)
                        SetState(EnemyAIState.Attack);
                    break;

                case EnemyAIState.Attack:
                    if (!_perception.HasTarget)
                        SetState(EnemyAIState.Idle);
                    else if (_perception.DistanceToTarget > config.attackRange)
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
                        _navigation.SetDestination(_perception.Target.transform.position);
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
                        Vector3 toTarget = _perception.Target.transform.position - _character.transform.position;
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
    }
}
