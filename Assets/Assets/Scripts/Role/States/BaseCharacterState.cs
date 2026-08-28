using UnityEngine;
using Role.StateMachine;

namespace Role.States
{
    /// <summary>
    /// 角色状态基类——所有具体状态继承此类
    /// 上下文由所属状态机在 ChangeState 时自动注入
    /// </summary>
    public abstract class BaseCharacterState
    {
        protected CharacterRoot character;
        protected Role.Core.CharacterStateCoordinator coordinator;
        protected BaseStateMachine stateMachine;

        /// <summary> 快捷访问角色 Animator（可能为 null） </summary>
        protected Animator Animator => character?.Animator;

        /// <summary> 由 BaseStateMachine.ChangeState 调用，注入运行上下文 </summary>
        public void SetContext(CharacterRoot character, Role.Core.CharacterStateCoordinator coordinator, BaseStateMachine stateMachine)
        {
            this.character = character;
            this.coordinator = coordinator;
            this.stateMachine = stateMachine;
        }

        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnExit() { }
    }
}
