using UnityEngine;
using Role.Core;

namespace Role.Controllers
{
    /// <summary>
    /// 音频控制器 Stub——响应角色状态变化播放对应音效
    /// </summary>
    public class AudioController : MonoBehaviour, IStateResponder
    {
        public void OnStateEnter(CharacterState state)
        {
            // TODO: 播放状态对应音效（死亡音效、眩晕音效等）
        }

        public void OnStateExit(CharacterState state) { }

        /// <summary> 由 Animator 同物体的 CharacterAnimatorEventRelay 转发脚步事件 </summary>
        public void PlayFootstep()
        {
            // TODO: 播放脚步音效（AudioSource.PlayOneShot(...)）
        }
    }
}
