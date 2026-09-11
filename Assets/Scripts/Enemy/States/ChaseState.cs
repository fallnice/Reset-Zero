namespace Enemy.States
{
    /// <summary>
    /// 追击——朝目标移动；看得见就追当前位置，看不见则追最后已知位置。
    /// 进入攻击距离且导航已到达时转 Attack；丢失目标转 Investigate；
    /// 离出生点过远时转 ReturnHome（归位优先级高于继续追击）。
    /// </summary>
    public sealed class ChaseState : EnemyStateBase
    {
        public override EnemyAIState Kind => EnemyAIState.Chase;

        public override void OnUpdate(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.Blackboard == null) return;

            // 先判归位，避免与 GetDesiredState 的「有目标就追」相互否决导致原地冻结
            if (context.IsFarFromHome())
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.ReturnHome);
                return;
            }

            if (!context.Blackboard.HasTarget)
            {
                StopMovement(context);
                context.RequestTransition(context.Blackboard.HasLastKnownPosition
                    ? EnemyAIState.Investigate
                    : EnemyAIState.ReturnHome);
                return;
            }

            Vector3 destination = context.Blackboard.HasLineOfSight
                ? context.Blackboard.Target.transform.position
                : context.Blackboard.LastKnownTargetPosition;
            MoveTo(context, destination);

            if (context.Blackboard.DistanceToTarget <= context.Config.attackRange
                && context.Navigation != null
                && context.Navigation.HasReachedDestination)
            {
                context.RequestTransition(EnemyAIState.Attack);
            }
        }
    }
}
