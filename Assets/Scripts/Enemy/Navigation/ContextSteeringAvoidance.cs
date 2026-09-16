using Role;
using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary>
    /// 轻量 Context Steering：在 A* 或直线导航给出的方向附近采样，综合目标兴趣、
    /// 静态障碍危险与同伴分离后输出局部修正方向。查询和评分均复用固定缓冲区。
    /// </summary>
    public sealed class ContextSteeringAvoidance : ILocalAvoidance
    {
        private const int DirectionCount = 16;
        private const int NeighborCapacity = 24;
        private const int ObstacleHitCapacity = 8;
        private const float DirectionEpsilon = 0.0001f;
        private const float SideBiasFactor = 0.25f;

        private static readonly Vector3[] Directions = BuildDirections();

        private readonly Collider[] _neighborBuffer = new Collider[NeighborCapacity];
        private readonly RaycastHit[] _obstacleHits = new RaycastHit[ObstacleHitCapacity];
        private readonly CharacterRoot[] _neighbors = new CharacterRoot[NeighborCapacity];
        private readonly float[] _danger = new float[DirectionCount];

        private Transform _agent;
        private CharacterController _controller;
        private EnemyConfig _config;
        private float _queryTimer;
        private int _neighborCount;
        private int _lastDirectionIndex = -1;

        /// <summary> 导航层提供的原始水平期望方向。 </summary>
        public Vector3 RawDirection { get; private set; }
        /// <summary> 局部避障修正后的最终水平方向。 </summary>
        public Vector3 AdjustedDirection { get; private set; }
        /// <summary> 最近一次查询识别到的有效邻居数。 </summary>
        public int NeighborCount => _neighborCount;
        /// <summary> 最近一次物理查询是否填满固定缓冲区。 </summary>
        public bool IsNeighborBufferSaturated { get; private set; }
        /// <summary> 当前局部避障运行状态。 </summary>
        public LocalAvoidanceStatus Status { get; private set; } = LocalAvoidanceStatus.Disabled;

        /// <summary> 注入角色、控制器和配置依赖。 </summary>
        public void Initialize(Transform agent, CharacterController controller, EnemyConfig config)
        {
            _agent = agent;
            _controller = controller;
            _config = config;
            Reset();
        }

        /// <summary> 根据附近障碍和角色修正本帧期望方向。 </summary>
        public Vector3 AdjustDirection(Vector3 desiredDirection, float deltaTime)
        {
            desiredDirection.y = 0f;
            RawDirection = desiredDirection.sqrMagnitude > DirectionEpsilon
                ? desiredDirection.normalized
                : Vector3.zero;

            if (_agent == null || _config == null || !_config.localAvoidanceEnabled)
            {
                AdjustedDirection = RawDirection;
                Status = LocalAvoidanceStatus.Disabled;
                return AdjustedDirection;
            }

            if (RawDirection.sqrMagnitude <= DirectionEpsilon)
            {
                AdjustedDirection = Vector3.zero;
                Status = LocalAvoidanceStatus.Clear;
                return AdjustedDirection;
            }

            _queryTimer -= Mathf.Max(0f, deltaTime);
            // 邻居查询与危险图构建涉及物理检测，按间隔降频；缓存失效时立即刷新，避免继续避让死亡对象。
            if (_queryTimer <= 0f || HasInvalidNeighbor())
            {
                RefreshNeighbors();
                BuildDangerMap();
                _queryTimer = _config.avoidanceQueryInterval;
            }
            int rawIndex = FindClosestDirection(RawDirection);
            if (_danger[rawIndex] <= _config.avoidanceActivationDanger)
            {
                _lastDirectionIndex = rawIndex;
                AdjustedDirection = RawDirection;
                Status = LocalAvoidanceStatus.Clear;
                return AdjustedDirection;
            }

            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            // 所有角色靠自身右侧通行；相向时各自右侧对应相反世界方向，可破除镜像会车。
            Vector3 preferredSide = Vector3.Cross(Vector3.up, RawDirection);
            for (int i = 0; i < DirectionCount; i++)
            {
                float forwardDot = Vector3.Dot(Directions[i], RawDirection);
                if (forwardDot < 0f) continue;

                float turnPenalty = 1f - forwardDot;
                float sideBias = Mathf.Max(0f, Vector3.Dot(Directions[i], preferredSide));
                float persistence = i == _lastDirectionIndex ? _config.avoidanceDirectionPersistence : 0f;
                float score = forwardDot * _config.avoidanceInterestWeight
                    - _danger[i] * _config.avoidanceDangerWeight
                    - turnPenalty * _config.avoidanceTurnPenalty
                    + sideBias * _config.avoidanceDirectionPersistence * SideBiasFactor
                    + persistence;

                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = i;
            }

            if (bestIndex < 0 || bestScore <= _config.avoidanceBlockedScore)
            {
                AdjustedDirection = Vector3.zero;
                Status = LocalAvoidanceStatus.Blocked;
                return AdjustedDirection;
            }

            _lastDirectionIndex = bestIndex;
            AdjustedDirection = Directions[bestIndex];
            Status = LocalAvoidanceStatus.Adjusting;
            return AdjustedDirection;
        }

        /// <summary> 清空方向、邻居和诊断状态。 </summary>
        public void Reset()
        {
            RawDirection = Vector3.zero;
            AdjustedDirection = Vector3.zero;
            _neighborCount = 0;
            IsNeighborBufferSaturated = false;
            _queryTimer = 0f;
            _lastDirectionIndex = -1;
            Status = _config != null && _config.localAvoidanceEnabled
                ? LocalAvoidanceStatus.Clear
                : LocalAvoidanceStatus.Disabled;
        }

        /// <summary> 检查降频缓存中是否出现销毁或死亡角色，决定是否提前刷新物理查询。 </summary>
        private bool HasInvalidNeighbor()
        {
            for (int i = 0; i < _neighborCount; i++)
            {
                CharacterRoot neighbor = _neighbors[i];
                if (neighbor == null || (neighbor.Health != null && neighbor.Health.IsDead))
                    return true;
            }
            return false;
        }

        /// <summary> 使用固定碰撞体缓冲查询附近敌人，并按角色根节点去重与过滤无效对象。 </summary>
        private void RefreshNeighbors()
        {
            _neighborCount = 0;
            IsNeighborBufferSaturated = false;
            float radius = _config.avoidanceNeighborRadius;
            if (radius <= 0f || _config.avoidanceAgentMask.value == 0) return;

            int colliderCount = Physics.OverlapSphereNonAlloc(
                _agent.position,
                radius,
                _neighborBuffer,
                _config.avoidanceAgentMask,
                QueryTriggerInteraction.Ignore);
            IsNeighborBufferSaturated = colliderCount >= _neighborBuffer.Length;

            for (int i = 0; i < colliderCount && _neighborCount < _neighbors.Length; i++)
            {
                Collider hit = _neighborBuffer[i];
                if (hit == null) continue;

                CharacterRoot root = hit.GetComponentInParent<CharacterRoot>();
                if (root == null || root.transform == _agent || root.IsPlayerControlled) continue;
                if (root.Health != null && root.Health.IsDead) continue;

                bool duplicate = false;
                for (int j = 0; j < _neighborCount; j++)
                {
                    if (_neighbors[j] != root) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate)
                    _neighbors[_neighborCount++] = root;
            }

            for (int i = _neighborCount; i < _neighbors.Length; i++)
                _neighbors[i] = null;
        }

        /// <summary> 为固定采样方向构建同伴与静态障碍危险值，供本轮方向评分复用。 </summary>
        private void BuildDangerMap()
        {
            float probeDistance = _config.avoidanceObstacleProbeDistance;
            float scaleX = Mathf.Abs(_agent.lossyScale.x);
            float scaleZ = Mathf.Abs(_agent.lossyScale.z);
            float scaleY = Mathf.Abs(_agent.lossyScale.y);
            float probeRadius = _controller != null
                ? Mathf.Max(0.05f, _controller.radius * Mathf.Max(scaleX, scaleZ)
                    * _config.avoidanceProbeRadiusFactor)
                : 0.25f;
            Vector3 origin = _agent.position + Vector3.up * (_controller != null
                ? _controller.center.y * scaleY
                : 0.6f);

            for (int i = 0; i < DirectionCount; i++)
            {
                Vector3 direction = Directions[i];
                float danger = CalculateNeighborDanger(direction);

                if (probeDistance > 0f && _config.navigationObstacleMask.value != 0)
                {
                    // 多命中查询用于跳过自身 Collider 后继续找到真实墙体；固定数组避免每轮采样产生 GC。
                    int hitCount = Physics.SphereCastNonAlloc(
                        origin,
                        probeRadius,
                        direction,
                        _obstacleHits,
                        probeDistance,
                        _config.navigationObstacleMask,
                        QueryTriggerInteraction.Ignore);
                    float nearestDistance = probeDistance;
                    bool foundObstacle = false;
                    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        Collider hitCollider = _obstacleHits[hitIndex].collider;
                        if (hitCollider == null || hitCollider.transform.IsChildOf(_agent)) continue;

                        nearestDistance = Mathf.Min(nearestDistance, _obstacleHits[hitIndex].distance);
                        foundObstacle = true;
                    }

                    if (foundObstacle)
                        danger += 1f - Mathf.Clamp01(nearestDistance / probeDistance);
                }

                _danger[i] = danger;
            }
        }

        /// <summary> 计算候选方向朝向邻近角色时的距离加权分离危险。 </summary>
        private float CalculateNeighborDanger(Vector3 candidateDirection)
        {
            float radius = _config.avoidanceNeighborRadius;
            if (radius <= 0f) return 0f;

            float danger = 0f;
            Vector3 position = _agent.position;
            for (int i = 0; i < _neighborCount; i++)
            {
                CharacterRoot neighbor = _neighbors[i];
                if (neighbor == null) continue;

                Vector3 toNeighbor = neighbor.transform.position - position;
                toNeighbor.y = 0f;
                float distance = toNeighbor.magnitude;
                if (distance <= DirectionEpsilon || distance >= radius) continue;

                float ahead = Mathf.Max(0f, Vector3.Dot(candidateDirection, toNeighbor / distance));
                if (ahead <= 0f) continue;

                float proximity = 1f - Mathf.Clamp01(distance / radius);
                danger += ahead * proximity * _config.avoidanceSeparationWeight;
            }
            return danger;
        }

        /// <summary> 找到与连续导航方向夹角最小的固定采样方向索引。 </summary>
        private static int FindClosestDirection(Vector3 direction)
        {
            int bestIndex = 0;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < DirectionCount; i++)
            {
                float dot = Vector3.Dot(Directions[i], direction);
                if (dot <= bestDot) continue;
                bestDot = dot;
                bestIndex = i;
            }
            return bestIndex;
        }

        /// <summary> 初始化均匀分布的水平采样方向；仅在类型首次加载时分配一次。 </summary>
        private static Vector3[] BuildDirections()
        {
            var directions = new Vector3[DirectionCount];
            float step = Mathf.PI * 2f / DirectionCount;
            for (int i = 0; i < DirectionCount; i++)
            {
                float angle = step * i;
                directions[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }
            return directions;
        }
    }
}
