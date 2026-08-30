using UnityEngine;
using Role.Core;

namespace Role.Controllers
{
    /// <summary>
    /// 表情控制器 Stub——控制面部 BlendShape 或 Sprite 表情
    /// </summary>
    public class ExpressionController : MonoBehaviour, IStateResponder
    {
        public void OnStateEnter(CharacterState state)
        {
            // TODO: 根据状态切换表情（正常/眩晕/死亡）
        }

        public void OnStateExit(CharacterState state) { }
    }
}
