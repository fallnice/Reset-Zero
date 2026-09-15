using UnityEngine;
using Enemy.Navigation;

namespace Enemy.States
{
    /// <summary>
    /// 调查——前往最后已知位置或听觉线索，到达后原地搜索一段时间。
    /// 期间发现目标立即转 Chase；搜索超时或怀疑度衰减到退出阈值以下转 ReturnHome。
    /// </summary>
    public sealed class InvestigateState : EnemyStateBase
    {
        // 退出阈值取进入阈值的比例，保证迟滞方向始终正确（退出阈值恒低于进入阈值）
        private const float EXIT_SUSPICION_RATIO = 0.4f;
        private const float SCAN_DEGREES_PER_SECOND = 120f;

        private bool _arrived;
        private float _arriveTime;
        private float _scanAngle;
        private Vector3 _scanForward;
        private int _searchPointsUsed;
        private bool _searchingSidePoint;
        private Vector3 _sidePoint;

        public override EnemyAIState Kind => EnemyAIState.Investigate;

        public override void OnEnter(EnemyAIContext context)
        {
            _arrived = false;
            _arriveTime = 0f;
            _scanAngle = 0f;
            _scanForward = Vector3.zero;
            _searchPointsUsed = 0;
            _searchingSidePoint = false;
            _sidePoint = Vector3.zero;
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

            // 离出生点太远时放弃搜索，回防区
            if (context.IsFarFromHome())
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.ReturnHome);
                return;
            }

            float exitSuspicion = context.Config.investigateSuspicionThreshold * EXIT_SUSPICION_RATIO;
            if (context.Blackboard.Suspicion < exitSuspicion)
            {
                StopMovement(context);
                context.Blackboard.ClearTarget();
                context.RequestTransition(EnemyAIState.ReturnHome);
                return;
            }

            if (_arrived)
            {
                StopMovement(context);
                ScanAround(context);
                if (Time.time - _arriveTime >= context.Config.investigateSeconds)
                {
                    // 扫完当前点：怀疑度还够且没搜完配额，就往可疑点周围再找一个点继续搜
                    // （绕墙角追踪的关键——只搜最后已知位置，目标一拐弯就跟丢了）
                    if (TryPickSearchPoint(context, out Vector3 next))
                    {
                        _searchPointsUsed++;
                        _searchingSidePoint = true;
                        _sidePoint = next;
                        _arrived = false;
                        _scanForward = Vector3.zero;
                        _scanAngle = 0f;
                        return;
                    }

                    // 搜索结束：清掉残留线索，否则黑板会一直认为「还记得位置」
                    context.Blackboard.ClearTarget();
                    context.Blackboard.ClearHeardClue();
                    context.RequestTransition(EnemyAIState.ReturnHome);
                }
                return;
            }

            // 既没有听觉线索也没有最后已知位置时，不去世界原点，直接放弃搜索
            if (!context.Blackboard.HasHeardClue
                && !context.Blackboard.HasLastKnownPosition
                && !_searchingSidePoint)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.ReturnHome);
                return;
            }

            Vector3 destination = _searchingSidePoint
                ? _sidePoint
                : context.Blackboard.HasHeardClue
                    ? context.Blackboard.LastHeardPosition
                    : context.Blackboard.LastKnownTargetPosition;
            // 必须真正走到可疑点：用战斗停止距离(1.5m)会停在离黄框一步之遥，视线仍被墙角挡住
            MoveTo(context, destination, context.Config.investigateArriveDistance);

            if (HasArrived(context, destination) || HasNavigationFailed(context))
            {
                StopMovement(context);
                _arrived = true;
                _arriveTime = Time.time;
                _searchingSidePoint = false;
                context.Blackboard.ClearHeardClue();
            }
        }

        /// <summary>
        /// 在最后已知位置（或听觉线索）周围挑一个可走的搜索点。
        /// 网格判可走时已含角色净空，取到的点不会卡进墙体。
        /// </summary>
        private bool TryPickSearchPoint(EnemyAIContext context, out Vector3 point)
        {
            point = Vector3.zero;
            EnemyNavigationGrid grid = context.Grid;
            if (grid == null || !grid.IsBuilt) return false;
            if (_searchPointsUsed >= context.Config.investigateMaxSearchPoints) return false;

            EnemyAIBlackboard blackboard = context.Blackboard;
            Vector3 anchor = blackboard.HasHeardClue
                ? blackboard.LastHeardPosition
                : blackboard.LastKnownTargetPosition;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized
                    * Random.Range(1f, context.Config.investigateSearchRadius);
                Vector3 candidate = anchor + new Vector3(offset.x, 0f, offset.y);
                if (!grid.TryGetNodeFromWorld(candidate, out int index) || !grid.IsWalkable(index)) continue;

                point = grid.GetNodePosition(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 到达可疑点后原地环视：站着不动只能看到来时的方向，
        /// 玩家绕到侧后方就永远不会被重新发现，搜索等于白等。
        /// 以到达时的朝向为基准单向扫，避免每帧叠加角度导致越转越快。
        /// </summary>
        private void ScanAround(EnemyAIContext context)
        {
            if (context == null || context.Character == null) return;

            if (_scanForward.sqrMagnitude < 0.0001f)
            {
                _scanForward = context.Character.transform.forward;
                _scanForward.y = 0f;
                if (_scanForward.sqrMagnitude < 0.0001f) _scanForward = Vector3.forward;
                _scanForward.Normalize();
            }

            _scanAngle += SCAN_DEGREES_PER_SECOND * context.DeltaTime;
            FaceDirection(context, Quaternion.Euler(0f, _scanAngle, 0f) * _scanForward);
        }

        public override void OnExit(EnemyAIContext context)
        {
            _arrived = false;
            _searchingSidePoint = false;
        }
    }
}
