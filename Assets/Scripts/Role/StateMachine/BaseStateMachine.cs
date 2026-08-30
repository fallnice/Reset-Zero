using UnityEngine;
using Role.States;
using Role.Core;

namespace Role.StateMachine
{
    /// <summary>
    /// 角色状态机基类——纯 C# 类，不继承 MonoBehaviour
    /// 由 CharacterRoot 在 Awake 中 new 创建，Update 中驱动
    /// </summary>
    public abstract class BaseStateMachine
    {
        protected CharacterRoot character;
        protected CharacterStateCoordinator coordinator;
        protected BaseCharacterState currentState;

        /// <summary> 初始化状态机，由 CharacterRoot.Start 调用 </summary>
        public virtual void Init(CharacterRoot character, CharacterStateCoordinator coordinator)
        {
            this.character = character;
            this.coordinator = coordinator;
        }

        /// <summary> 每帧驱动当前状态，由 CharacterRoot.Update 调用 </summary>
        public void OnUpdate()
        {
            currentState?.OnUpdate();
        }

        /// <summary> 切换状态：先退出旧状态，再注入上下文，再进入新状态 </summary>
        public void ChangeState(BaseCharacterState newState)
        {
            currentState?.OnExit();
            currentState = newState;
            // 注入上下文——让状态能访问 character、coordinator 和所属状态机
            currentState.SetContext(character, coordinator, this);
            currentState.OnEnter();
        }

        public T GetCurrentState<T>() where T : BaseCharacterState
        {
            return currentState as T;
        }
    }
}
