using UnityEngine;

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

        private bool _arrived;
        private float _arriveTime;

        public override EnemyAIState Kind => EnemyAIState.Investigate;

        public override void OnEnter(EnemyAIContext context)
        {
            _arrived = false;
            _arriveTime = 0f;
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
                if (Time.time - _arriveTime >= context.Config.investigateSeconds)
                {
                    // 搜索结束：清掉残留线索，否则黑板会一直认为「还记得位置」
                    context.Blackboard.ClearTarget();
                    context.Blackboard.ClearHeardClue();
                    context.RequestTransition(EnemyAIState.ReturnHome);
                }
                return;
            }

            // 既没有听觉线索也没有最后已知位置时，不去世界原点，直接放弃搜索
            if (!context.Blackboard.HasHeardClue && !context.Blackboard.HasLastKnownPosition)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.ReturnHome);
                return;
            }

            Vector3 destination = context.Blackboard.HasHeardClue
                ? context.Blackboard.LastHeardPosition
                : context.Blackboard.LastKnownTargetPosition;
            MoveTo(context, destination);

            if (HasArrived(context, destination) || HasNavigationFailed(context))
            {
                StopMovement(context);
                _arrived = true;
                _arriveTime = Time.time;
                context.Blackboard.ClearHeardClue();
            }
        }

        public override void OnExit(EnemyAIContext context)
        {
            _arrived = false;
        }
    }
}
