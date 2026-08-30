using UnityEngine;
using Role.Core;

namespace Role.StateMachine
{
    /// <summary>
    /// 上半身叠加状态机 Stub——负责瞄准/射击/使用物品等上半身动作
    /// 通过 Animator Layer + Avatar Mask 与 FullBody 状态机叠加
    /// </summary>
    public class UpperBodyStateMachine : BaseStateMachine
    {
        // TODO: 上半身叠加状态逻辑（瞄准/射击/使用物品）
        // 当前复用 BaseStateMachine.OnUpdate() 驱动当前状态
    }
}
