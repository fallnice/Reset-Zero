using UnityEngine;

namespace Enemy.States
{
    /// <summary>
    /// 巡逻——在出生点周围随机取可达点往返；发现目标立即转 Chase。
    /// 未配置导航网格，或取不到有效巡逻点时退化为 Idle，不产生无意义移动。
    /// </summary>
    public sealed class PatrolState : EnemyStateBase
    {
        private Vector3 _patrolTarget;
        private bool _hasDestination;

        public override EnemyAIState Kind => EnemyAIState.Patrol;

        /// <summary> 当前巡逻目标点（Gizmos 与调试用） </summary>
        public Vector3 PatrolTarget => _patrolTarget;

        public override void OnEnter(EnemyAIContext context)
        {
            _hasDestination = TryPickPatrolPoint(context);
        }

        public override void OnUpdate(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.Blackboard == null) return;

            if (context.Blackboard.HasTarget)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Chase);
                return;
            }
            if (context.Blackboard.Suspicion >= context.Config.investigateSuspicionThreshold)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Investigate);
                return;
            }

            if (!_hasDestination)
                _hasDestination = TryPickPatrolPoint(context);

            // 取不到可走点（无网格或周围全是障碍）就回 Idle，避免原地反复重试
            if (!_hasDestination)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Idle);
                return;
            }

            MoveTo(context, _patrolTarget);

            if (HasArrived(context, _patrolTarget) || HasNavigationFailed(context))
            {
                StopMovement(context);
                _hasDestination = false;
                context.RequestTransition(EnemyAIState.Idle);
            }
        }

        public override void OnExit(EnemyAIContext context)
        {
            _hasDestination = false;
        }

        /// <summary> 在出生点半径内随机取点，并吸附到最近可走节点；过近的点会被丢弃 </summary>
        private bool TryPickPatrolPoint(EnemyAIContext context)
        {
            if (context == null || context.Character == null || context.Config == null) return false;

            Navigation.EnemyNavigationGrid grid = context.Grid;
            if (grid == null || !grid.EnsureBuilt()) return false;

            Vector3 selfPosition = context.Character.transform.position;
            Vector3 home = context.HomePosition;
            float minDistanceSqr = context.Config.patrolMinPointDistance * context.Config.patrolMinPointDistance;

            for (int i = 0; i < context.Config.patrolPickAttempts; i++)
            {
                Vector2 offset = Random.insideUnitCircle * context.Config.patrolRadius;
                Vector3 candidate = new Vector3(home.x + offset.x, home.y, home.z + offset.y);

                if (!grid.TryFindNearestWalkable(candidate, grid.EndpointSearchRadius, out int nodeIndex))
                    continue;

                Vector3 position = grid.GetNodePosition(nodeIndex);
                Vector3 delta = position - selfPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistanceSqr) continue;

                _patrolTarget = position;
                return true;
            }

            return false;
        }
    }
}
