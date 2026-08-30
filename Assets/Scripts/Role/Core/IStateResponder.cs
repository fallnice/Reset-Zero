namespace Role.Core
{
    /// <summary>
    /// 角色状态响应器接口——所有子控制器实现此接口以响应角色状态变化
    /// </summary>
    public interface IStateResponder
    {
        /// <summary> 角色进入某状态时调用 </summary>
        void OnStateEnter(CharacterState state);

        /// <summary> 角色退出某状态时调用 </summary>
        void OnStateExit(CharacterState state);
    }

    /// <summary> 角色全局状态枚举 </summary>
    public enum CharacterState
    {
        Normal,     // 正常（可移动、可操作）
        Dead,       // 死亡（Ragdoll、UI 只读）
        Stunned,    // 眩晕（位移停止、输入忽略）
        Dialogue,   // 对话中（移动停止、Bag 只读）
        Mounted,    // 骑乘中（替换移动逻辑）
        Cutscene,   // 过场（所有操作阻断，UI 隐藏）
    }
}
