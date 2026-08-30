namespace Role.Core
{
    /// <summary>
    /// 输入抽象接口——玩家和 AI 共用同一套输入契约
    /// Character 只依赖此接口，不关心输入来源
    /// </summary>
    public interface IInputProvider
    {
        /// <summary> 世界空间移动方向（已归一化） </summary>
        UnityEngine.Vector3 MoveDirection { get; }

        /// <summary> 世界空间注视/瞄准方向 </summary>
        UnityEngine.Vector3 LookDirection { get; }

        /// <summary> 本帧是否按下了跳跃 </summary>
        bool JumpPressed { get; }

        /// <summary> 本帧是否按下了射击/使用 </summary>
        bool ActionPressed { get; }

        /// <summary> 本帧是否按下了交互键（边缘触发，按下当帧有效） </summary>
        bool InteractPressed { get; }

        /// <summary> 本帧是否按下了冲刺 </summary>
        bool SprintPressed { get; }

        /// <summary> 本帧是否有任何输入（用于 Idle 检测） </summary>
        bool HasAnyInput { get; }
    }
}
