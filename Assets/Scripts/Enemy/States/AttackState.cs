namespace Enemy.States
{
    /// <summary>
    /// 攻击——停下面向目标，按 AI 层冷却提交攻击请求。
    /// 实际命中与武器冷却仍由 EquipmentController 负责，这里只发「想攻击」的意图。
    /// 目标脱离攻击范围转 Chase；目标丢失转 Investigate 或 ReturnHome。
    /// </summary>
    public sealed class AttackState : EnemyStateBase
    {
        public override EnemyAIState Kind => EnemyAIState.Attack;

        public override void OnEnter(EnemyAIContext context)
        {
            StopMovement(context);
        }

        public override void OnUpdate(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.Blackboard == null) return;
            if (context.Character == null || !context.Blackboard.HasTarget) return;

            if (context.Blackboard.DistanceToTarget > context.Config.attackRange)
            {
                StopMovement(context);
                context.RequestTransition(EnemyAIState.Chase);
                return;
            }

            // 近战命中方向取决于角色朝向，攻击前先对准目标
            Vector3 toTarget = context.Blackboard.Target.transform.position
                - context.Character.transform.position;
            FaceDirection(context, toTarget);
            TryRequestAttack(context);
        }
    }
}
