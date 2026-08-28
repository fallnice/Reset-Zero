using UnityEngine;
using Role.StateMachine;
using Role.Core;

namespace Role.States.FullBody
{
    /// <summary>
    /// 待机状态——速度归零，施加重力保持贴地，有输入转 Walk/Run
    /// </summary>
    public class IdleState : BaseCharacterState
    {
        private CharacterController _cc;
        private float _gravity = -20f;
        private float _verticalVelocity;

        public override void OnEnter()
        {
            _cc = character.GetComponent<CharacterController>();
            Blackboard.Set("MoveSpeed", 0f);

            if (Animator != null)
            {
                Animator.SetFloat("Speed", 0f);
                Animator.SetBool("IsGrounded", true);
            }
        }

        public override void OnUpdate()
        {
            ApplyGravity();

            if (character == null || character.inputProvider == null) return;

            var input = character.inputProvider;

            if (input.JumpPressed)
            {
                character.fullBodySM.ToJump();
                return;
            }

            if (input.MoveDirection.sqrMagnitude > 0.01f)
            {
                if (input.SprintPressed)
                    character.fullBodySM.ToRun();
                else
                    character.fullBodySM.ToWalk();
            }
        }

        private void ApplyGravity()
        {
            if (_cc == null) return;
            _verticalVelocity = _cc.isGrounded && _verticalVelocity < 0f
                ? -2f
                : _verticalVelocity + _gravity * Time.deltaTime;
            _cc.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }
    }
}
