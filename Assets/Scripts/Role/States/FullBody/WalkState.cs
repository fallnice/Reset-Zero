using UnityEngine;
using Role.StateMachine;

namespace Role.States.FullBody
{
    /// <summary>
    /// 行走状态——按输入方向移动，速度 = walkSpeed，施加重力保持贴地
    /// </summary>
    public class WalkState : BaseCharacterState
    {
        private CharacterController _cc;
        private float _verticalVelocity;

        public override void OnEnter()
        {
            _cc = character.GetComponent<CharacterController>();
            character.Context.Runtime.moveSpeed = character.Config.walkSpeed;

            character.SetLocomotionAnimation(0.5f, true);
        }

        public override void OnUpdate()
        {
            if (character == null || character.inputProvider == null) return;

            var input = character.inputProvider;
            var dir = input.MoveDirection;

            // 移动 + 重力
            if (_cc != null)
            {
                Vector3 velocity = dir * character.Config.walkSpeed;
                _verticalVelocity = _cc.isGrounded && _verticalVelocity < 0f
                    ? character.Config.groundedStickForce
                    : _verticalVelocity + character.Config.gravity * Time.deltaTime;
                velocity.y = _verticalVelocity;
                _cc.Move(velocity * Time.deltaTime);
            }

            // 角色朝向跟随移动方向（相机转向时角色自然跟着转）；瞄准时由 CharacterRoot 锁定相机方向
            character.RotateByMovement(dir);

            if (input.JumpPressed)
            {
                character.fullBodySM.ToJump();
                return;
            }

            // 瞄准时不允许起跑：瞄准套只有走路动画，跑起来必然滑步
            if (input.SprintPressed && !character.IsAiming && dir.sqrMagnitude > 0.01f)
            {
                character.fullBodySM.ToRun();
                return;
            }

            if (dir.sqrMagnitude < 0.01f)
            {
                character.fullBodySM.ToIdle();
            }
        }
    }
}
