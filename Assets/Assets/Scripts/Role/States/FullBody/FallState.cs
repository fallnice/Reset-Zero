using UnityEngine;
using Role.StateMachine;
using Role.Core;

namespace Role.States.FullBody
{
    /// <summary>
    /// 下落状态——继承跳跃的垂直速度，重力加速，触地后回到 Idle/Walk/Run
    /// </summary>
    public class FallState : BaseCharacterState
    {
        private CharacterController _cc;
        private float _verticalVelocity;

        public override void OnEnter()
        {
            _cc = character.GetComponent<CharacterController>();
            _verticalVelocity = Blackboard.Get<float>("Air_VerticalVelocity", 0f);

            if (Animator != null)
                Animator.SetBool("IsGrounded", false);
        }

        public override void OnUpdate()
        {
            if (_cc == null) return;

            // 水平移动（空中可控）
            var dir = character.inputProvider != null
                ? character.inputProvider.MoveDirection
                : Vector3.zero;
            float speed = Blackboard.Get<float>("MoveSpeed", 3.5f);

            Vector3 velocity = dir * speed;
            _verticalVelocity += character.Config.gravity * Time.deltaTime;
            velocity.y = _verticalVelocity;
            _cc.Move(velocity * Time.deltaTime);

            // 空中也可以调整朝向
            if (dir.sqrMagnitude > 0.01f)
                character.RotateToward(dir);

            // 触地 → 根据输入回到对应状态
            if (_cc.isGrounded && _verticalVelocity < 0f)
            {
                if (Animator != null)
                    Animator.SetBool("IsGrounded", true);

                if (character.inputProvider != null && character.inputProvider.MoveDirection.sqrMagnitude > 0.01f)
                {
                    if (character.inputProvider.SprintPressed)
                        character.fullBodySM.ToRun();
                    else
                        character.fullBodySM.ToWalk();
                }
                else
                {
                    character.fullBodySM.ToIdle();
                }
            }
        }
    }
}
