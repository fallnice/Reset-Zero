using UnityEngine;

namespace Enemy.States
{
    /// <summary>
    /// 敌人状态基类——提供移动、转向、攻击请求等通用动作。
    /// 状态只调用这些动作，不直接写 Transform 或 CharacterController。
    /// </summary>
    public abstract class EnemyStateBase : IEnemyState
    {
        public abstract EnemyAIState Kind { get; }

        public virtual void OnEnter(EnemyAIContext context) { }
        public virtual void OnUpdate(EnemyAIContext context) { }
        public virtual void OnExit(EnemyAIContext context) { }

        /// <summary> 驱动导航并把移动意图写入 AI 输入；不可移动时只停止，不 Tick 导航 </summary>
        protected static void MoveTo(EnemyAIContext context, Vector3 destination)
        {
            if (context == null || context.Navigation == null) return;

            context.Navigation.SetDestination(destination);

            if (context.Character != null && !context.Character.CanMove)
            {
                // 死亡/眩晕等强状态下暂停 Tick，避免静止被误诊为卡住
                context.AiInput?.SetMoveDirection(Vector3.zero);
                return;
            }

            context.Navigation.Tick(context.DeltaTime);
            Vector3 move = context.Navigation.MoveDirection;
            context.AiInput?.SetMoveDirection(move);
            if (move.sqrMagnitude > 0.0001f)
                context.AiInput?.SetLookDirection(move);
        }

        /// <summary> 停止移动并清空导航 </summary>
        protected static void StopMovement(EnemyAIContext context)
        {
            context?.StopMovement();
        }

        /// <summary> 转向水平方向；用于攻击前对准目标 </summary>
        protected static void FaceDirection(EnemyAIContext context, Vector3 direction)
        {
            if (context == null || context.Character == null) return;

            Vector3 flat = direction;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;

            flat.Normalize();
            context.Character.RotateToward(flat);
            context.AiInput?.SetLookDirection(flat);
            context.AiInput?.SetMoveDirection(Vector3.zero);
        }

        /// <summary> 按 AI 层冷却提交一次攻击请求；实际攻击由 EquipmentController 执行 </summary>
        protected static void TryRequestAttack(EnemyAIContext context)
        {
            if (context == null || context.Config == null || context.AiInput == null) return;
            if (Time.time < context.NextAttackTime) return;

            context.AiInput.SetAttackPressed(true);
            context.NextAttackTime = Time.time + context.Config.attackCooldown;
        }

        /// <summary>
        /// 是否已到达某个水平位置。
        /// 同时校验物理距离，避免导航把「吸附后的路径终点」误判为到达目标本身。
        /// </summary>
        protected static bool HasArrived(EnemyAIContext context, Vector3 position)
        {
            if (context == null || context.Character == null || context.Config == null) return false;
            if (context.Navigation == null || !context.Navigation.HasReachedDestination) return false;

            float tolerance = Mathf.Max(
                context.Config.navigationStoppingDistance,
                context.Config.waypointReachDistance) * 3f;

            Vector3 delta = context.Character.transform.position - position;
            delta.y = 0f;
            return delta.sqrMagnitude <= tolerance * tolerance;
        }

        /// <summary> 导航连续失败/卡住超过阈值，用于 Patrol/ReturnHome 兜底逃逸 </summary>
        protected static bool HasNavigationFailed(EnemyAIContext context)
        {
            if (context == null || context.Config == null) return false;
            return context.NavigationFailedTime >= context.Config.navigationFailureTimeout;
        }
    }
}
