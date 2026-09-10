using UnityEngine;
using Role.StateMachine;

namespace Role.States.FullBody
{
    /// <summary>
    /// 跑步状态——按输入方向移动，速度 = runSpeed，施加重力保持贴地
    /// </summary>
    public class RunState : BaseCharacterState
    {
        private CharacterController _cc;
        private float _verticalVelocity;

        public override void OnEnter()
        {
            _cc = character.GetComponent<CharacterController>();
            character.Context.Runtime.moveSpeed = character.Config.runSpeed;

            if (Animator != null)
            {
                Animator.SetFloat("Speed", 1f);
                Animator.SetBool("IsGrounded", true);
            }
        }

        public override void OnUpdate()
        {
            if (character == null || character.inputProvider == null) return;

            var input = character.inputProvider;
            var dir = input.MoveDirection;

            // 移动 + 重力
            if (_cc != null)
            {
                Vector3 velocity = dir * character.Config.runSpeed;
                _verticalVelocity = _cc.isGrounded && _verticalVelocity < 0f
                    ? character.Config.groundedStickForce
                    : _verticalVelocity + character.Config.gravity * Time.deltaTime;
                velocity.y = _verticalVelocity;
                _cc.Move(velocity * Time.deltaTime);
            }

            // 角色朝向跟随移动方向（相机转向时角色自然跟着转）；瞄准时由 CharacterRoot 锁定相机方向
            character.RotateByMovement(dir);

            // 跳跃 → Jump
            if (input.JumpPressed)
            {
                character.fullBodySM.ToJump();
                return;
            }

            // 进入瞄准 → 退回 Walk（瞄准套无跑动动画，继续跑会滑步）
            if (character.IsAiming)
            {
                character.fullBodySM.ToWalk();
                return;
            }

            // 冲刺松开 → Walk
            if (!input.SprintPressed && dir.sqrMagnitude > 0.01f)
            {
                character.fullBodySM.ToWalk();
                return;
            }

            // 无输入 → Idle
            if (dir.sqrMagnitude < 0.01f)
            {
                character.fullBodySM.ToIdle();
            }
        }
    }
}
