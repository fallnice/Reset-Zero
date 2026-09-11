using UnityEngine;

namespace Role.Controllers
{
    /// <summary>
    /// Animator 同物体事件桥——接收 Animation Event 与 Root Motion 回调，再转发给角色表现控制器。
    /// CharacterRoot 会在运行时确保 Animator 所在物体存在本组件，无需依赖 Prefab 手工挂载。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimatorEventRelay : MonoBehaviour
    {
        private CharacterAnimationController _animationController;
        private AudioController _audioController;

        /// <summary> 由 CharacterRoot.Awake 显式初始化，避免依赖组件 Awake 顺序 </summary>
        public void Initialize(CharacterRoot character)
        {
            if (character == null) return;
            _animationController = character.GetComponentInChildren<CharacterAnimationController>(true);
            _audioController = character.GetComponentInChildren<AudioController>(true);
        }

        private void Awake()
        {
            Initialize(GetComponentInParent<CharacterRoot>());
        }

        /// <summary> 接收 CombatGirls 动画包的插槽/IK 命令 </summary>
        public void SwitchSocket(AnimationEvent animEvent)
        {
            if (animEvent == null) return;
            _animationController?.SwitchSocketByString(animEvent.stringParameter);
        }

        /// <summary> 接收移动剪辑自带的脚步 Animation Event </summary>
        public void PlayFootSound()
        {
            _audioController?.PlayFootstep();
        }

        /// <summary> 接管 Animator Root Motion，角色位移继续由 CharacterController 驱动 </summary>
        private void OnAnimatorMove() { }
    }
}
