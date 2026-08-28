using UnityEngine;
using Role.StateMachine;
using Role.Core;

namespace Role.States.FullBody
{
    /// <summary>
    /// 跳跃状态——施加向上初速度，重力减速，速度转负后进入 Fall
    /// </summary>
    public class JumpState : BaseCharacterState
    {
        private CharacterController _cc;
        private float _verticalVelocity;

        public override void OnEnter()
        {
            _cc = character.GetComponent<CharacterController>();
            _verticalVelocity = character.Config.jumpForce;

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

            // 速度转负 → Fall（传递当前垂直速度）
            if (_verticalVelocity < 0f)
            {
                Blackboard.Set("Air_VerticalVelocity", _verticalVelocity);
                character.fullBodySM.ToFall();
            }
        }
    }
}
