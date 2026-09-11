namespace Enemy
{
    /// <summary>
    /// 敌人 AI 状态——第四阶段 HFSM 的可插拔状态单元。
    ///
    /// 约定：状态只负责「进入/每帧逻辑/退出」，不直接写 Transform；
    /// 移动一律通过 Navigation → AIInputProvider → CharacterController 执行。
    /// 状态迁移通过 context 的黑板与配置判断，由 EnemyBrain 统一切换。
    /// </summary>
    public interface IEnemyState
    {
        EnemyAIState Kind { get; }

        void OnEnter(EnemyAIContext context);
        void OnUpdate(EnemyAIContext context);
        void OnExit(EnemyAIContext context);
    }
}
