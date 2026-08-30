using System.Collections.Generic;
using UnityEngine;
using Core;

namespace Role.Core
{
    /// <summary>
    /// 角色状态协调器——集中管理角色状态变更，统一通知所有子控制器
    /// 不直接控制任何控制器，只负责状态分发
    /// </summary>
    public class CharacterStateCoordinator : MonoBehaviour
    {
        private CharacterState _current = CharacterState.Normal;
        private readonly List<IStateResponder> _responders = new List<IStateResponder>();

        public CharacterState CurrentState => _current;

        /// <summary> 注册状态响应器（子控制器在 Awake/Start 中调用） </summary>
        public void Register(IStateResponder responder)
        {
            if (!_responders.Contains(responder))
                _responders.Add(responder);
        }

        /// <summary> 注销状态响应器（OnDestroy 中调用） </summary>
        public void Unregister(IStateResponder responder)
        {
            _responders.Remove(responder);
        }

        /// <summary> 切换角色状态，自动通知所有响应器 </summary>
        public void ChangeState(CharacterState newState)
        {
            if (_current == newState) return;

            var old = _current;
            _current = newState;

            // 先退出旧状态
            for (int i = _responders.Count - 1; i >= 0; i--)
            {
                _responders[i].OnStateExit(old);
            }

            // 广播事件（UI 层等其他系统监听）
            EventBus.Emit(EventName.Character_StateChanged, old, newState);

            // 再进入新状态
            for (int i = 0; i < _responders.Count; i++)
            {
                _responders[i].OnStateEnter(newState);
            }
        }

        // -------- 便捷查询（子控制器可直接调用）--------

        public bool IsNormal => _current == CharacterState.Normal;
        public bool IsDead => _current == CharacterState.Dead;
        public bool CanMove => _current == CharacterState.Normal || _current == CharacterState.Mounted;
        public bool CanOpenBag => _current == CharacterState.Normal;
        public bool CanAttack => _current == CharacterState.Normal || _current == CharacterState.Mounted;
    }
}
