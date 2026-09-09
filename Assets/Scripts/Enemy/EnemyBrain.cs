using UnityEngine;
using Role;

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
    /// 复用 CharacterRoot 的移动/战斗链路，不直接操作状态机或武器；
    /// 决策只产出「移动方向 + 是否攻击」，与玩家输入同构。
    /// </summary>
    [RequireComponent(typeof(AIInputProvider))]
    public class EnemyBrain : MonoBehaviour
    {
        [SerializeField] private EnemyConfig config;

        private CharacterRoot _character;
        private AIInputProvider _aiInput;
        private readonly EnemyPerception _perception = new EnemyPerception();
        private DirectNavigation _navigation;

        private EnemyAIState _state = EnemyAIState.Idle;
        private float _nextAttackTime;

        /// <summary> 当前决策状态（供调试/表现层查询） </summary>
        public EnemyAIState CurrentState => _state;

        private void Awake()
        {
            _character = GetComponent<CharacterRoot>();
            _aiInput = GetComponent<AIInputProvider>();
            _navigation = new DirectNavigation(config);
        }

        private void Start()
        {
            // 开局装备初始近战武器，让攻击链路可复用（equipmentCtrl 已在 CharacterRoot.Awake 完成 Init）
            if (config != null && config.meleeWeapon != null && _character != null)
                _character.Equipment?.Pickup(config.meleeWeapon);
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
                    else if (_perception.DistanceToTarget <= config.attackRange)
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

        /// <summary> 执行当前状态：把意图写入 AIInputProvider，交由 CharacterRoot 消费 </summary>
        private void ExecuteState()
        {
            switch (_state)
            {
                case EnemyAIState.Idle:
                    _aiInput.SetMoveDirection(Vector3.zero);
                    _aiInput.SetLookDirection(_character.transform.forward);
                    break;

                case EnemyAIState.Chase:
                    {
                        Vector3 move = _navigation.ComputeMoveDirection(
                            _character.transform, _perception.Target.transform.position);
                        _aiInput.SetMoveDirection(move);
                        _aiInput.SetLookDirection(move);
                    }
                    break;

                case EnemyAIState.Attack:
                    {
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
