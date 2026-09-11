namespace Enemy.States
{
    /// <summary>
    /// 死亡——终态，只保持停止输出，等待 CharacterRoot.Respawn / Health.ResetHealth 后重回 Idle。
    /// </summary>
    public sealed class DeadState : EnemyStateBase
    {
        public override EnemyAIState Kind => EnemyAIState.Dead;

        public override void OnEnter(EnemyAIContext context)
        {
            StopMovement(context);
        }

        public override void OnUpdate(EnemyAIContext context)
        {
            StopMovement(context);
        }
    }
}
