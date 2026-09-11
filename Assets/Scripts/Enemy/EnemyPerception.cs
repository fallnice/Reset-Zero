using Role;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 敌人感知——负责回答「敌人知道什么」，把事实写入 EnemyAIBlackboard。
    ///
    /// 本阶段（4.2）在距离检测之上补三件事：
    ///   1. 视线（LOS）：视野角 + 射线遮挡；
    ///   2. 最后已知位置：目标脱离视线后，Investigate 仍有目的地；
    ///   3. 怀疑度与听觉线索：为 HFSM 的 Investigate 提供进入依据。
    ///
    /// 感知只写数据，不决定行为；是否追击、调查或归位由 EnemyBrain/HFSM 决定。
    /// 候选目标来自 EnemyRegistry，不做全场景扫描。
    /// </summary>
    public class EnemyPerception
    {
        private CharacterRoot _target;
        private float _distanceToTarget;
        private float _nextSearchTime;
        private float _suspicion;
        private bool _losMaskWarned;

        /// <summary> 当前锁定目标；无目标时为 null </summary>
        public CharacterRoot Target => _target;

        /// <summary> 是否已锁定目标 </summary>
        public bool HasTarget => _target != null;

        /// <summary> 到当前目标的距离（仅在有目标时有效） </summary>
        public float DistanceToTarget => _distanceToTarget;

        /// <summary> 每帧更新：推进黑板计时 → 刷新目标 → 计算视线/怀疑度 → 写入黑板 </summary>
        public void Update(CharacterRoot self, EnemyConfig config, EnemyAIBlackboard blackboard)
        {
            if (config == null || blackboard == null) return;

            blackboard.BeginUpdate(self);

            if (_target != null)
                RefreshExistingTarget(self, config);
            else
                TryAcquire(self, config);

            blackboard.SetTarget(_target, _distanceToTarget);

            if (_target != null)
                UpdateVisibility(self, config, blackboard);
            else
                blackboard.SetLineOfSight(false, Vector3.zero);

            UpdateSuspicion(self, config, blackboard);
            UpdateTacticalInfo(self, config, blackboard);
        }

        /// <summary> 已锁定目标时刷新距离；目标死亡或超出丢失距离则脱锁 </summary>
        private void RefreshExistingTarget(CharacterRoot self, EnemyConfig config)
        {
            // 目标死亡则脱锁，避免盯着尸体
            if (_target.Health != null && _target.Health.IsDead)
            {
                _target = null;
                _distanceToTarget = 0f;
                return;
            }

            _distanceToTarget = Vector3.Distance(self.transform.position, _target.transform.position);
            if (_distanceToTarget > config.loseTargetRange)
            {
                _target = null;
                _distanceToTarget = 0f;
            }
        }

        /// <summary> 无目标时按节流间隔从注册表查找最近的存活玩家 </summary>
        private void TryAcquire(CharacterRoot self, EnemyConfig config)
        {
            if (Time.time < _nextSearchTime) return;
            _nextSearchTime = Time.time + config.perceptionSearchInterval;

            if (!EnemyRegistry.TryGetNearestPlayer(
                    self.transform.position, config.detectionRange, self, out CharacterRoot candidate, out float d))
                return;

            _target = candidate;
            _distanceToTarget = d;
        }

        /// <summary> 计算视线（距离 + 视野角 + 射线遮挡）并写入最后已知位置 </summary>
        private void UpdateVisibility(CharacterRoot self, EnemyConfig config, EnemyAIBlackboard blackboard)
        {
            bool visible = IsVisible(self, config, _target, out Vector3 seenPosition);
            blackboard.SetLineOfSight(visible, seenPosition);
        }

        /// <summary>
        /// 目标是否可见：距离内 → 视野角内 → 视线未被遮挡。
        /// LOS 掩码为 0 时只告警一次并退化成「不遮挡」，避免把所有碰撞体都当墙。
        /// </summary>
        private bool IsVisible(CharacterRoot self, EnemyConfig config, CharacterRoot target, out Vector3 seenPosition)
        {
            seenPosition = target.transform.position;
            if (_distanceToTarget > config.detectionRange) return false;
            if (!IsInsideViewAngle(self, config, seenPosition)) return false;

            if (config.losObstacleMask.value == 0)
            {
                if (!_losMaskWarned)
                {
                    _losMaskWarned = true;
                    Debug.LogWarning("[EnemyPerception] LOS Obstacle Mask 为空，视线检测退化为不被遮挡", self);
                }
                return true;
            }

            Vector3 origin = self.transform.position + Vector3.up * config.eyeHeight;
            Vector3 toTarget = seenPosition + Vector3.up * config.eyeHeight - origin;
            float distance = toTarget.magnitude;
            if (distance < 0.001f) return true;

            // 打到目标自身算看见，因此允许射线略超过目标距离
            return !Physics.Raycast(origin, toTarget / distance, out RaycastHit hit,
                distance + config.losTargetTolerance, config.losObstacleMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary> 目标是否落在视野角内 </summary>
        private static bool IsInsideViewAngle(CharacterRoot self, EnemyConfig config, Vector3 targetPosition)
        {
            if (config.viewAngle >= 179.5f) return true;

            Vector3 toTarget = targetPosition - self.transform.position;
            toTarget.y = 0f;
            Vector3 forward = self.transform.forward;
            forward.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return true;

            return Vector3.Angle(forward.normalized, toTarget.normalized) <= config.viewAngle * 0.5f;
        }

        /// <summary>
        /// 怀疑度：看得见目标 → 拉满；看不见但记得位置 → 维持，
        /// 超过记忆时长后按 decayPerSecond 衰减，为 Investigate 退出提供依据。
        /// </summary>
        private void UpdateSuspicion(CharacterRoot self, EnemyConfig config, EnemyAIBlackboard blackboard)
        {
            if (blackboard.HasLineOfSight)
            {
                _suspicion = 1f;
            }
            else if (blackboard.HasTarget)
            {
                // 锁定但暂时看不见：先维持，超时再衰减
                if (blackboard.TimeSinceLastSeen > config.targetMemorySeconds && config.suspicionDecayPerSecond > 0f)
                    _suspicion = Mathf.Max(0f, _suspicion - config.suspicionDecayPerSecond * Time.deltaTime);
                else
                    _suspicion = Mathf.Max(_suspicion, config.investigateSuspicionThreshold);
            }
            else
            {
                _suspicion = Mathf.Max(0f, _suspicion - config.suspicionDecayPerSecond * Time.deltaTime);
            }

            blackboard.SetSuspicion(_suspicion);
        }

        /// <summary>
        /// 掩体与威胁的初步评估。
        /// 掩体按脚下到目标方向是否被遮挡判断；威胁按自身生命粗略分级。
        /// 后续接入 Influence Map 时可替换为更精细的采样。
        /// </summary>
        private void UpdateTacticalInfo(CharacterRoot self, EnemyConfig config, EnemyAIBlackboard blackboard)
        {
            Vector3 origin = self.transform.position + Vector3.up * config.eyeHeight;
            bool blocked = false;
            if (_target != null && config.losObstacleMask.value != 0)
            {
                Vector3 toTarget = _target.transform.position + Vector3.up * config.eyeHeight - origin;
                float distance = toTarget.magnitude;
                if (distance > 0.001f)
                {
                    blocked = Physics.Raycast(origin, toTarget / distance, out RaycastHit _,
                        distance, config.losObstacleMask, QueryTriggerInteraction.Ignore);
                }
            }

            CoverStatus cover = blocked ? CoverStatus.Full : CoverStatus.None;
            ThreatLevel threat = blackboard.HealthRatio <= 0.35f ? ThreatLevel.High : ThreatLevel.Low;
            blackboard.SetTacticalInfo(cover, threat);
        }
    }
}
