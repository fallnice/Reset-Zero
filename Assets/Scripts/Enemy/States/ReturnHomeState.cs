using UnityEngine;

namespace Enemy.States
{
    /// <summary>
    /// 归位——返回出生点，避免在追击/调查中被无限拉离防区。
    /// 到家后转 Idle；途中发现目标立即转 Chase；导航持续失败时放弃归位转 Idle。
    /// </summary>
    public sealed class ReturnHomeState : EnemyStateBase
    {
        public override EnemyAIState Kind => EnemyAIState.ReturnHome;

        public override void OnUpdate(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.Blackboard == null) return;

            // 必须与 GetDesiredState 的 ReturnHome 分支一致：
            // 还没回到防区就继续归位，否则会与 Chase 的「离家过远」判定互顶，导致边界上原地冻结。
            if (context.Blackboard.HasTarget && !context.IsFarFromHome())
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Chase);
                return;
            }

            MoveTo(context, context.HomePosition);

            if (HasArrived(context, context.HomePosition) || HasNavigationFailed(context))
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Idle);
            }
        }
    }
}
