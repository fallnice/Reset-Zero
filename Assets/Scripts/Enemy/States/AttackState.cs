using UnityEngine;

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

            // 前摇期间持续面向目标；命中帧到来时近战范围会按最新朝向判定，而不是锁死起手方向。
            Vector3 toTarget = context.Blackboard.Target.transform.position
                - context.Character.transform.position;
            FaceDirection(context, toTarget);
            TryRequestAttack(context);
        }

        /// <summary> 离开攻击态时取消尚未到达命中帧的攻击，防止追击、眩晕或死亡后仍补出伤害 </summary>
        public override void OnExit(EnemyAIContext context)
        {
            context?.Character?.Equipment?.CancelPendingAttack();
        }
    }
}
