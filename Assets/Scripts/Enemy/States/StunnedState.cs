namespace Enemy.States
{
    /// <summary>
    /// 眩晕——强状态由 CharacterRoot 的 Coordinator 统一控制，这里只停止输出。
    /// 眩晕期间不 Tick 导航，避免静止被误诊为卡住；解除后由 EnemyBrain 回到 Idle。
    /// </summary>
    public sealed class StunnedState : EnemyStateBase
    {
        public override EnemyAIState Kind => EnemyAIState.Stunned;

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
