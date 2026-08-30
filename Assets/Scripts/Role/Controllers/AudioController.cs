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

        /// <summary>
        /// 动画事件回调——走路/跑步脚步声
        /// 动画clip自带的 AnimationEvent "PlayFootSound" 会自动调用此方法
        /// </summary>
        public void PlayFootSound()
        {
            // TODO: 播放脚步音效（AudioSource.PlayOneShot(...)）
        }
    }
}
