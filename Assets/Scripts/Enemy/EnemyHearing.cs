using Combat;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 敌人听觉——把「受击」和「附近武器声」写进黑板，是 EnemyAIBlackboard.SetHeardClue 的生产者。
    ///
    /// 为什么单独一个组件：
    ///   EnemyPerception 只处理视觉（距离 + 视野角 + 视线）；听觉是被动事件流，
    ///   两者数据来源完全不同，混在一起会让感知层既难读又难调。
    ///
    /// 当前实现（4.2 补齐，够用）：
    ///   1. 受击：订阅本角色 HealthController.DamagedWithContext，伤害来源在听觉范围内就记录；
    ///   2. 武器声：由 EnemyBrain 在 AttackCommitted 时调用 NotifyWeaponNoise()，
    ///      用攻击者当前位置近似声源（全局 EventBus 不带位置，无法优雅订阅）。
    ///
    /// TODO(后续)：战斗层补一个带攻击者位置的 Weapon_Fired / Noise 事件，
    /// 这里改成订阅该事件，就能支持「消音武器听不到」「不同武器不同半径」与真实声源位置。
    /// 届时 EnemyBrain 里的 NotifyWeaponNoise 调用可以删除。
    /// </summary>
    public class EnemyHearing : MonoBehaviour
    {
        private EnemyAIBlackboard _blackboard;
        private HealthController _health;
        private EnemyConfig _config;
        private bool _subscribed;

        /// <summary> 由 EnemyBrain 在 Awake 注入依赖，避免依赖组件 Awake 顺序 </summary>
        public void Initialize(EnemyAIBlackboard blackboard, HealthController health, EnemyConfig config)
        {
            _blackboard = blackboard;
            _health = health;
            _config = config;
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        /// <summary>
        /// 尝试订阅受击事件。
        /// EnemyBrain 执行顺序早于 CharacterRoot，Awake 时可能还拿不到 HealthController，
        /// 因此这里允许失败；Brain 在 Start（所有 Awake 之后）会再次调用补订。
        /// </summary>
        public void TrySubscribe()
        {
            if (_subscribed || _health == null) return;

            _health.DamagedWithContext += HandleDamagedWithContext;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed && _health != null)
            {
                _health.DamagedWithContext -= HandleDamagedWithContext;
                _subscribed = false;
            }
        }

        /// <summary> 受击：伤害来源在听觉范围内就记录线索；自己的伤害不算「听到动静」 </summary>
        private void HandleDamagedWithContext(DamageContext context)
        {
            if (context.attacker == null) return;
            if (context.attacker.transform == transform) return;

            RaiseNoise(context.attacker.transform.position,
                _config != null ? _config.damageSuspicionBoost : 0.6f);
        }

        /// <summary> 由 EnemyBrain 转发：其它角色开火产生的声音 </summary>
        public void NotifyHeardWeaponNoise(Vector3 sourcePosition)
        {
            NotifyWeaponNoise(sourcePosition, transform.position);
        }

        /// <summary>
        /// 外部武器声：由 EnemyBrain 在本角色提交攻击时调用。
        /// 会忽略自己发出的声音（自己的攻击不该把自己吓一跳），
        /// 也忽略听觉范围外的声源。
        ///
        /// TODO(后续)：战斗层补 Weapon_Fired 事件后，这里改成只处理真实开火，
        /// 并按武器类型决定是否可听（消音武器、近战挥砍不应产生枪声级别的声音）。
        /// </summary>
        public void NotifyWeaponNoise(Vector3 sourcePosition, Vector3 selfPosition)
        {
            if (_config == null) return;

            Vector3 toSource = sourcePosition - selfPosition;
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.01f) return;

            RaiseNoise(sourcePosition, _config.weaponNoiseSuspicionBoost);
        }

        /// <summary> 记录一处声源；超出听觉范围直接忽略 </summary>
        public void RaiseNoise(Vector3 position, float suspicionBoost)
        {
            if (_blackboard == null || _config == null) return;
            if (_config.hearingRange <= 0f) return;

            Vector3 delta = position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > _config.hearingRange * _config.hearingRange) return;

            _blackboard.SetHeardClue(position);
            if (suspicionBoost > 0f)
                _blackboard.AddSuspicion(suspicionBoost);
        }
    }
}
