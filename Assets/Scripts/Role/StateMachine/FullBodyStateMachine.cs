using UnityEngine;
using Role.States.FullBody;

namespace Role.StateMachine
{
    /// <summary>
    /// 全身状态机——负责位移相关状态（Idle/Walk/Run/Jump/Fall）
    /// </summary>
    public class FullBodyStateMachine : BaseStateMachine
    {
        // -------- 状态切换快捷方法（供 Input 或 Animation Event 调用）--------

        public void ToIdle()    { ChangeState(new States.FullBody.IdleState()); }
        public void ToWalk()    { ChangeState(new States.FullBody.WalkState()); }
        public void ToRun()     { ChangeState(new States.FullBody.RunState()); }
        public void ToJump()    { ChangeState(new States.FullBody.JumpState()); }
        public void ToFall()    { ChangeState(new States.FullBody.FallState()); }
    }
}
