using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary>
    /// 每个敌人的 A* 路径跟随器——负责目标阈值、重算节流、路径点推进、失败/卡住诊断。
    /// 自身没有 Update，由 EnemyBrain 主动 Tick；只输出移动方向，不直接移动角色。
    /// </summary>
    public class GridAStarNavigation : MonoBehaviour, IEnemyNavigation
    {
        [SerializeField] private EnemyNavigationGrid grid;
        [SerializeField] private bool drawPath = true;

        private Transform _agent;
        private EnemyConfig _config;
        private int[] _path;
        private int _pathCount;
        private int _waypointIndex;
        private Vector3 _destination;
        private Vector3 _plannedDestination;
        private Vector3 _moveDirection;
        private bool _hasDestination;
        private float _nextRepathTime;
        private float _stuckSampleTimer;
        private float _noProgressTime;
        private Vector3 _lastProgressPosition;
        private EnemyNavigationFailure _lastLoggedFailure = EnemyNavigationFailure.None;

        public Vector3 MoveDirection => _moveDirection;
        public Vector3 Destination => _destination;
        public EnemyNavigationStatus Status { get; private set; } = EnemyNavigationStatus.Idle;
        public EnemyNavigationFailure LastFailure { get; private set; } = EnemyNavigationFailure.None;
        public bool HasReachedDestination => Status == EnemyNavigationStatus.Reached;
        public bool HasFailed => Status == EnemyNavigationStatus.Failed;
        public bool IsStuck => Status == EnemyNavigationStatus.Stuck;

        public void Initialize(Transform agent, CharacterController controller, EnemyConfig config)
        {
            _agent = agent;
            _config = config;
            if (controller == null)
                Debug.LogWarning("[GridAStarNavigation] 缺少 CharacterController，角色无法执行导航移动", this);
            if (grid == null)
                grid = FindObjectOfType<EnemyNavigationGrid>();

            if (grid != null && grid.EnsureBuilt())
                _path = new int[grid.NodeCount];

            // 错开不同敌人的首轮重算时刻，避免同帧集中寻路。
            float phase = Mathf.Abs(GetInstanceID() % 100) * 0.001f;
            _nextRepathTime = Time.time + phase;
            _lastProgressPosition = agent != null ? agent.position : Vector3.zero;
        }

        public void SetDestination(Vector3 destination)
        {
            _destination = destination;
            _hasDestination = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_hasDestination || _agent == null || _config == null)
            {
                _moveDirection = Vector3.zero;
                return;
            }
            if (grid == null || !grid.EnsureBuilt() || grid.Pathfinder == null)
            {
                SetFailure(EnemyNavigationStatus.Failed, EnemyNavigationFailure.GridUnavailable);
                return;
            }
            if (_path == null || _path.Length != grid.NodeCount)
                _path = new int[grid.NodeCount];

            float now = Time.time;
            bool destinationMoved = HorizontalSqrDistance(_destination, _plannedDestination)
                >= _config.targetMoveThreshold * _config.targetMoveThreshold;
            bool needsPath = _pathCount == 0 || destinationMoved
                || Status == EnemyNavigationStatus.Failed || Status == EnemyNavigationStatus.Stuck;

            if (needsPath && now >= _nextRepathTime)
                TryRepath(now);

            if (_pathCount == 0 || Status == EnemyNavigationStatus.Failed || Status == EnemyNavigationStatus.Stuck)
            {
                _moveDirection = Vector3.zero;
                return;
            }

            float stoppingDistance = Mathf.Min(_config.navigationStoppingDistance, _config.attackRange);
            float destinationSqrDistance = HorizontalSqrDistance(_agent.position, _destination);

            // 最后一段已进入攻击停止距离时视为到达，不强迫角色挤进玩家占据的终点格中心。
            // 仍要求走到最后一段，避免隔墙直线距离很近时提前进入攻击状态。
            if (_waypointIndex >= Mathf.Max(0, _pathCount - 1)
                && destinationSqrDistance <= stoppingDistance * stoppingDistance)
            {
                MarkReached();
                return;
            }

            AdvanceWaypoints();
            if (_waypointIndex >= _pathCount)
            {
                // A* 可能把不可走终点吸附到邻近可走格；走完实际路径即结束，
                // Brain 仍会用 attackRange 决定是否攻击，不会无限重算同一路径。
                MarkReached();
                return;
            }

            Vector3 toWaypoint = grid.GetNodePosition(_path[_waypointIndex]) - _agent.position;
            toWaypoint.y = 0f;
            _moveDirection = toWaypoint.sqrMagnitude > 0.0001f ? toWaypoint.normalized : Vector3.zero;
            Status = EnemyNavigationStatus.Moving;
            UpdateStuckDetection(deltaTime, now);
        }

        public void Stop()
        {
            _hasDestination = false;
            _pathCount = 0;
            _waypointIndex = 0;
            _moveDirection = Vector3.zero;
            _stuckSampleTimer = 0f;
            _noProgressTime = 0f;
            Status = EnemyNavigationStatus.Idle;
            LastFailure = EnemyNavigationFailure.None;
        }

        private void TryRepath(float now)
        {
            _nextRepathTime = now + (Status == EnemyNavigationStatus.Failed
                ? _config.failedRetryInterval
                : _config.repathInterval);

            bool success = grid.Pathfinder.TryFindPath(
                _agent.position,
                _destination,
                _config.maxSearchNodes,
                _path,
                out _pathCount,
                out EnemyNavigationFailure failure);

            if (!success)
            {
                _pathCount = 0;
                SetFailure(EnemyNavigationStatus.Failed, failure);
                return;
            }

            _plannedDestination = _destination;
            _waypointIndex = 0;
            _stuckSampleTimer = 0f;
            _noProgressTime = 0f;
            _lastProgressPosition = _agent.position;
            Status = EnemyNavigationStatus.Moving;
            LastFailure = EnemyNavigationFailure.None;
            _lastLoggedFailure = EnemyNavigationFailure.None;
            AdvanceWaypoints();
        }

        private void AdvanceWaypoints()
        {
            float reachSqr = _config.waypointReachDistance * _config.waypointReachDistance;
            while (_waypointIndex < _pathCount
                && HorizontalSqrDistance(_agent.position, grid.GetNodePosition(_path[_waypointIndex])) <= reachSqr)
            {
                _waypointIndex++;
            }
        }

        private void MarkReached()
        {
            Status = EnemyNavigationStatus.Reached;
            LastFailure = EnemyNavigationFailure.None;
            _moveDirection = Vector3.zero;
            _stuckSampleTimer = 0f;
            _noProgressTime = 0f;
        }

        private void UpdateStuckDetection(float deltaTime, float now)
        {
            if (_moveDirection.sqrMagnitude < 0.01f) return;

            _stuckSampleTimer += deltaTime;
            if (_stuckSampleTimer < _config.stuckSampleInterval) return;

            float sampleDuration = _stuckSampleTimer;
            _stuckSampleTimer = 0f;
            float progressSqr = HorizontalSqrDistance(_agent.position, _lastProgressPosition);
            if (progressSqr < _config.stuckMinProgress * _config.stuckMinProgress)
                _noProgressTime += sampleDuration;
            else
                _noProgressTime = 0f;

            _lastProgressPosition = _agent.position;
            if (_noProgressTime < _config.stuckTimeout) return;

            _pathCount = 0;
            _nextRepathTime = now + _config.repathInterval;
            SetFailure(EnemyNavigationStatus.Stuck, EnemyNavigationFailure.NoProgress);
        }

        private void SetFailure(EnemyNavigationStatus status, EnemyNavigationFailure failure)
        {
            Status = status;
            LastFailure = failure;
            _moveDirection = Vector3.zero;
            if (_lastLoggedFailure == failure) return;

            _lastLoggedFailure = failure;
            Debug.LogWarning($"[GridAStarNavigation] {name} 导航失败：{failure}", this);
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return x * x + z * z;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawPath) return;

            Gizmos.color = Status == EnemyNavigationStatus.Failed
                ? Color.red
                : Status == EnemyNavigationStatus.Stuck ? new Color(1f, 0.45f, 0f) : Color.cyan;

            if (_agent != null && grid != null && _path != null && _pathCount > 0)
            {
                Vector3 previous = _agent.position;
                for (int i = Mathf.Max(0, _waypointIndex); i < _pathCount; i++)
                {
                    Vector3 point = grid.GetNodePosition(_path[i]) + Vector3.up * 0.08f;
                    Gizmos.DrawLine(previous, point);
                    Gizmos.DrawSphere(point, 0.08f);
                    previous = point;
                }
            }

            if (_hasDestination)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_destination, 0.2f);
            }
        }
    }
}
