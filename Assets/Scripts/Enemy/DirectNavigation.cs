using UnityEngine;
using Enemy.Navigation;

namespace Enemy
{
    /// <summary>
    /// 直线导航——IEnemyNavigation 的兼容回退实现。
    /// 旧 Prefab 未挂 GridAStarNavigation 时仍可使用；只适合无障碍小场地。
    /// </summary>
    public class DirectNavigation : IEnemyNavigation
    {
        private Transform _agent;
        private EnemyConfig _config;
        private Vector3 _destination;
        private bool _hasDestination;

        public Vector3 MoveDirection { get; private set; }
        public Vector3 Destination => _destination;
        public EnemyNavigationStatus Status { get; private set; } = EnemyNavigationStatus.Idle;
        public EnemyNavigationFailure LastFailure => EnemyNavigationFailure.None;
        public bool HasReachedDestination => Status == EnemyNavigationStatus.Reached;
        public bool HasFailed => false;
        public bool IsStuck => false;

        public DirectNavigation(EnemyConfig config)
        {
            _config = config;
        }

        public void Initialize(Transform agent, CharacterController controller, EnemyConfig config)
        {
            _agent = agent;
            _config = config;
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
                MoveDirection = Vector3.zero;
                return;
            }

            Vector3 toTarget = _destination - _agent.position;
            toTarget.y = 0f;
            float stoppingDistance = Mathf.Min(_config.navigationStoppingDistance, _config.attackRange);
            if (toTarget.sqrMagnitude <= stoppingDistance * stoppingDistance)
            {
                MoveDirection = Vector3.zero;
                Status = EnemyNavigationStatus.Reached;
                return;
            }

            Vector3 direction = toTarget.normalized;
            Vector3 origin = _agent.position + Vector3.up * 0.6f;
            if (_config.navigationObstacleMask.value != 0
                && Physics.Raycast(origin, direction, out RaycastHit _, _config.obstacleAvoidDistance,
                    _config.navigationObstacleMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 right = Vector3.Cross(Vector3.up, direction);
                direction = (direction + right * 0.8f).normalized;
            }

            MoveDirection = direction;
            Status = EnemyNavigationStatus.Moving;
        }

        public void Stop()
        {
            _hasDestination = false;
            MoveDirection = Vector3.zero;
            Status = EnemyNavigationStatus.Idle;
        }
    }
}
