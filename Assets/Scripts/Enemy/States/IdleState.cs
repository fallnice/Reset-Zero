namespace Enemy.States
{
    /// <summary>
    /// 待机——原地警戒，朝向保持不变。
    /// 有目标转 Chase；怀疑度达到阈值转 Investigate；静止超过 idleSeconds 转 Patrol 巡逻。
    /// </summary>
    public sealed class IdleState : EnemyStateBase
    {
        private float _elapsed;

        public override EnemyAIState Kind => EnemyAIState.Idle;

        public override void OnEnter(EnemyAIContext context)
        {
            _elapsed = 0f;
            StopMovement(context);
        }

        public override void OnUpdate(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.Blackboard == null) return;

            StopMovement(context);
            _elapsed += context.DeltaTime;

            if (context.Blackboard.HasTarget)
            {
                context.RequestTransition(EnemyAIState.Chase);
                return;
            }
            if (context.Blackboard.Suspicion >= context.Config.investigateSuspicionThreshold)
            {
                context.RequestTransition(EnemyAIState.Investigate);
                return;
            }
            if (_elapsed >= context.Config.idleSeconds)
                context.RequestTransition(EnemyAIState.Patrol);
        }
    }
}
