using UnityEngine;
using Role.Core;

namespace Role.Controllers
{
    /// <summary>
    /// IK 控制器 Stub——处理手部 IK、LookAt、死亡时切换 Ragdoll
    /// </summary>
    public class IKController : MonoBehaviour, IStateResponder
    {
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponentInParent<Animator>();
        }

        public void OnStateEnter(CharacterState state)
        {
            if (state == CharacterState.Dead)
            {
                // TODO: 切换 Ragdoll
            }
        }

        public void OnStateExit(CharacterState state) { }

        // 在 Animator 之后执行（LateUpdate）
        public void OnLateUpdate()
        {
            if (_animator == null) return;
            // TODO: 应用手部 IK、LookAt
        }
    }
}
