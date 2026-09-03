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

        /// <summary> 本帧鼠标/右摇杆旋转增量（用于第三人称相机旋转，未归一化） </summary>
        UnityEngine.Vector2 LookDelta { get; }

        /// <summary> 本帧是否按下了跳跃 </summary>
        bool JumpPressed { get; }

        /// <summary> 本帧是否按下攻击键（边缘触发，按下当帧有效） </summary>
        bool AttackPressedThisFrame { get; }

        /// <summary> 攻击键是否持续按住（全自动武器使用） </summary>
        bool AttackHeld { get; }

        /// <summary> 本帧是否选择主武器槽（数字键 1） </summary>
        bool SelectPrimaryPressedThisFrame { get; }

        /// <summary> 本帧是否选择副武器槽（数字键 2） </summary>
        bool SelectSecondaryPressedThisFrame { get; }

        /// <summary> 本帧是否选择近战槽（数字键 3） </summary>
        bool SelectMeleePressedThisFrame { get; }

        /// <summary> 本帧是否按下了交互键（边缘触发，按下当帧有效） </summary>
        bool InteractPressed { get; }

        /// <summary> 本帧是否按下了冲刺 </summary>
        bool SprintPressed { get; }

        /// <summary> 本帧是否按下丢弃武器键（边缘触发，按下当帧有效） </summary>
        bool DropWeaponPressedThisFrame { get; }

        /// <summary> 本帧是否有任何输入（用于 Idle 检测） </summary>
        bool HasAnyInput { get; }
    }
}
