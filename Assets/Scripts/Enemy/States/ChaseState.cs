using UnityEngine;

namespace Enemy.States
{
    /// <summary>
    /// 追击——朝目标（或最后已知位置）移动，进入攻击距离且导航到达后转 Attack。
    ///
    /// 4.3 之后这里不只负责「冲上去」，还要执行 Utility 选出的打法：
    ///   Engage  贴上去打（等于 4.1 之前的默认行为）
    ///   Hold    保持对峙距离，原地面向目标等冷却
    ///   Flank   先绕到侧翼点，再回到 Engage
    ///   Retreat 后撤拉开距离，边退边面向目标
    ///
    /// 注意：是否继续战斗由 HFSM 决定（见 EnemyBrain.GetDesiredState），
    /// 这里只处理「同样在追击时怎么打」，不负责切到调查或归位。
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

            // 每帧重算战术：结果写进黑板，下面按选择执行不同走位
            context.Tactical?.Evaluate(context);

            if (!ExecuteTactic(context))
                return;

            bool reachedAttackPosition = context.Navigation != null
                && context.Navigation.HasReachedDestination;

            // 导航持续失败时也要能打：否则敌人站在攻击距离内却因为寻路失败一动不动
            if (context.Blackboard.DistanceToTarget <= context.Config.attackRange
                && (reachedAttackPosition || HasNavigationFailed(context)))
            {
                context.RequestTransition(EnemyAIState.Attack);
            }
        }

        /// <summary>
        /// 执行当前战术。返回 false 表示本帧已经申请了迁移，调用方不要再继续判定。
        /// </summary>
        private bool ExecuteTactic(EnemyAIContext context)
        {
            EnemyAIBlackboard blackboard = context.Blackboard;
            Vector3 targetPosition = blackboard.Target.transform.position;

            switch (blackboard.TacticalChoice)
            {
                case EnemyTacticalChoice.Retreat:
                    MoveTo(context, blackboard.RetreatPoint);
                    FaceTarget(context, targetPosition);
                    return false;

                case EnemyTacticalChoice.Flank:
                    MoveTo(context, blackboard.FlankPoint);

                    // 绕到侧翼点附近就转回压上，并开始计包抄冷却
                    if (HasArrived(context, blackboard.FlankPoint))
                    {
                        context.FlankCooldownRemaining = context.Config.flankCooldownSeconds;
                        blackboard.ForceTactical(EnemyTacticalChoice.Engage, 0f);
                        return false;
                    }

                    // 超时或导航失败就放弃包抄，避免卡在绕后路上
                    if (context.TacticalElapsed >= context.Config.flankTimeoutSeconds
                        || HasNavigationFailed(context))
                    {
                        context.FlankCooldownRemaining = context.Config.flankCooldownSeconds;
                        blackboard.ForceTactical(EnemyTacticalChoice.Engage, 0f);
                    }
                    return false;

                case EnemyTacticalChoice.Hold:
                    return ExecuteHold(context, targetPosition);

                case EnemyTacticalChoice.Engage:
                default:
                    Vector3 destination = blackboard.HasLineOfSight
                        ? targetPosition
                        : blackboard.LastKnownTargetPosition;
                    MoveTo(context, destination);
                    return true;
            }
        }

        /// <summary>
        /// 对峙：保持 holdDistanceFactor × attackRange 的距离。
        /// 太远就补一点位，太近就退一点，区间内原地面向目标等冷却。
        /// </summary>
        private bool ExecuteHold(EnemyAIContext context, Vector3 targetPosition)
        {
            EnemyAIBlackboard blackboard = context.Blackboard;
            if (context.Character == null) return false;

            float desired = context.Config.attackRange * context.Config.holdDistanceFactor;
            Vector3 away = context.Character.transform.position - targetPosition;
            away.y = 0f;

            if (away.sqrMagnitude < 0.0001f)
            {
                FaceTarget(context, targetPosition);
                return false;
            }

            if (away.magnitude < desired * 0.9f)
            {
                // 太近：往后退到对峙距离，同时保持面向目标
                MoveTo(context, targetPosition + away.normalized * desired);
                FaceTarget(context, targetPosition);
                return false;
            }

            if (away.magnitude > desired * 1.1f)
            {
                // 太远：往里补到对峙距离
                MoveTo(context, targetPosition + away.normalized * desired);
                return false;
            }

            StopMovement(context);
            FaceTarget(context, targetPosition);
            return false;
        }

        /// <summary> 原地转向目标；近战命中方向取决于角色朝向，撤退/对峙也要保持面向 </summary>
        private static void FaceTarget(EnemyAIContext context, Vector3 targetPosition)
        {
            if (context == null || context.Character == null) return;

            Vector3 toTarget = targetPosition - context.Character.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            context.Character.RotateToward(toTarget.normalized);
        }
    }
}
