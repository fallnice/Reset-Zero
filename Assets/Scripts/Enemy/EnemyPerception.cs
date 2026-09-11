using UnityEngine;
using Role;

namespace Enemy
{
    /// <summary>
    /// 敌人感知——按距离检测目标（玩家 CharacterRoot），锁定后缓存直到超出丢失距离。
    /// 候选目标来自 EnemyRegistry，避免周期性 FindObjectsOfType 的数组分配。
    /// 垂直切片只做距离检测；视线遮挡（LOS）、最后已知位置、威胁评估留待第四阶段扩展。
    /// </summary>
    public class EnemyPerception
    {
        private CharacterRoot _target;
        private float _distanceToTarget;
        private float _nextSearchTime;

        /// <summary> 当前锁定目标；无目标时为 null </summary>
        public CharacterRoot Target => _target;

        /// <summary> 是否已锁定目标 </summary>
        public bool HasTarget => _target != null;

        /// <summary> 到当前目标的距离（仅在有目标时有效） </summary>
        public float DistanceToTarget => _distanceToTarget;

        /// <summary> 每帧更新：无目标时搜索，有目标时刷新距离并按丢失距离脱锁 </summary>
        public void Update(CharacterRoot self, EnemyConfig config)
        {
            if (config == null) return;

            if (_target == null)
            {
                // 无目标时按固定间隔搜索，避免多个待机敌人每帧 FindObjectsOfType 产生数组分配
                if (Time.time >= _nextSearchTime)
                {
                    _nextSearchTime = Time.time + config.perceptionSearchInterval;
                    TryAcquire(self, config.detectionRange);
                }
            }
            else
            {
                // 目标死亡则脱锁，避免盯着尸体
                if (_target.Health != null && _target.Health.IsDead)
                {
                    _target = null;
                    return;
                }

                _distanceToTarget = Vector3.Distance(self.transform.position, _target.transform.position);
                if (_distanceToTarget > config.loseTargetRange)
                    _target = null;
            }
        }

        /// <summary> 在感知范围内查找存活玩家（注册表已排除自身、非玩家与死亡角色） </summary>
        private void TryAcquire(CharacterRoot self, float range)
        {
            if (!EnemyRegistry.TryGetNearestPlayer(
                    self.transform.position, range, self, out CharacterRoot candidate, out float d))
                return;

            _target = candidate;
            _distanceToTarget = d;
        }
    }
}
