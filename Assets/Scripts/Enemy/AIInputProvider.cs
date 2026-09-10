using UnityEngine;
using Role.Core;

namespace Enemy
{
    /// <summary>
    /// AI 输入提供者——把 EnemyBrain 的决策输出翻译成 IInputProvider，
    /// 复用 CharacterRoot 的移动/战斗链路（AI 与玩家共用同一输入契约）。
    /// 攻击边缘标志不在此处自动清除，而由 EnemyBrain 每帧决策前重置、决策后设置，
    /// 避免 LateUpdate 清除与 CharacterRoot.Update 读取之间的时序竞态。
    /// </summary>
    public class AIInputProvider : MonoBehaviour, IInputProvider
    {
        private Vector3 _moveDirection;
        private Vector3 _lookDirection = Vector3.forward;
        private bool _attackPressedThisFrame;

        /// <summary> 由 EnemyBrain 写入本帧移动方向（世界空间，已归一化） </summary>
        public void SetMoveDirection(Vector3 direction) => _moveDirection = direction;

        /// <summary> 由 EnemyBrain 写入本帧注视方向（世界空间） </summary>
        public void SetLookDirection(Vector3 direction) => _lookDirection = direction;

        /// <summary> 由 EnemyBrain 写入本帧攻击意图（边缘触发，下帧决策前会被重置） </summary>
        public void SetAttackPressed(bool pressed) => _attackPressedThisFrame = pressed;

        // ===== IInputProvider =====

        public Vector3 MoveDirection => _moveDirection;
        public Vector3 LookDirection => _lookDirection;
        public Vector2 LookDelta => Vector2.zero;
        public bool JumpPressed => false;
        public bool AttackPressedThisFrame => _attackPressedThisFrame;
        public bool AttackHeld => false;
        /// <summary> AI 无瞄准键概念，恒为 false </summary>
        public bool AimHeld => false;
        public bool SelectPrimaryPressedThisFrame => false;
        public bool SelectSecondaryPressedThisFrame => false;
        public bool SelectMeleePressedThisFrame => false;
        public bool InteractPressed => false;
        public bool SprintPressed => false;
        public bool DropWeaponPressedThisFrame => false;
        public bool HasAnyInput => _moveDirection.sqrMagnitude > 0.01f || _attackPressedThisFrame;
    }
}
