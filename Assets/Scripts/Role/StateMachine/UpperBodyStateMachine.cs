using Role.Core;
using Role.States;

namespace Role.StateMachine
{
    /// <summary> 上半身持续姿态语义，不包含具体 Animator 状态名 </summary>
    public enum UpperBodyMode
    {
        Inactive,
        RangedReady
    }

    /// <summary> 上半身瞬时动作语义，由动画适配层映射到 Trigger/动画片段 </summary>
    public enum UpperBodyAction
    {
        Fire,
        Reload,
        UseItem
    }

    /// <summary>
    /// 上半身动画输出接口——状态机只发送语义，不直接依赖 Animator 参数、Layer 或动画资源
    /// </summary>
    public interface IUpperBodyAnimationSink
    {
        /// <summary> 应用持续上半身姿态 </summary>
        void SetUpperBodyMode(UpperBodyMode mode);

        /// <summary> 播放一次瞬时上半身动作 </summary>
        void PlayUpperBodyAction(UpperBodyAction action);
    }

    /// <summary>
    /// 上半身叠加状态机——管理持续姿态与瞬时动作，通过可选动画 Sink 对接 Animator
    /// </summary>
    public class UpperBodyStateMachine : BaseStateMachine
    {
        private IUpperBodyAnimationSink _animationSink;
        private UpperBodyMode _requestedMode = UpperBodyMode.Inactive;
        private UpperBodyMode _currentMode = UpperBodyMode.Inactive;
        private bool _isSuppressed;

        public UpperBodyMode RequestedMode => _requestedMode;
        public UpperBodyMode CurrentMode => _currentMode;
        public bool IsSuppressed => _isSuppressed;

        /// <summary> 无动画 Sink 的兼容初始化入口 </summary>
        public override void Init(CharacterRoot character, CharacterStateCoordinator coordinator)
        {
            Init(character, coordinator, null);
        }

        /// <summary> 初始化状态机并注入可选动画输出；无动画资源时传 null 仍可正常运行 </summary>
        public void Init(
            CharacterRoot character,
            CharacterStateCoordinator coordinator,
            IUpperBodyAnimationSink animationSink)
        {
            base.Init(character, coordinator);
            _animationSink = animationSink;
            _requestedMode = UpperBodyMode.Inactive;
            _currentMode = UpperBodyMode.Inactive;
            _isSuppressed = false;
            ApplyMode(true);
        }

        /// <summary> 请求持续姿态；被抑制时记住请求，恢复后自动切回 </summary>
        public void SetMode(UpperBodyMode mode)
        {
            if (_requestedMode == mode) return;

            _requestedMode = mode;
            ApplyMode(false);
        }

        /// <summary> 死亡、眩晕等禁攻状态下临时关闭上半身叠加 </summary>
        public void SetSuppressed(bool isSuppressed)
        {
            if (_isSuppressed == isSuppressed) return;

            _isSuppressed = isSuppressed;
            ApplyMode(false);
        }

        /// <summary> 尝试播放瞬时语义动作；不在合法姿态或被抑制时忽略 </summary>
        public bool TryPlayAction(UpperBodyAction action)
        {
            if (_isSuppressed) return false;
            if ((action == UpperBodyAction.Fire || action == UpperBodyAction.Reload)
                && _currentMode != UpperBodyMode.RangedReady)
                return false;

            _animationSink?.PlayUpperBodyAction(action);
            return true;
        }

        /// <summary> 将请求姿态与抑制状态合并为最终有效姿态 </summary>
        private void ApplyMode(bool force)
        {
            UpperBodyMode effectiveMode = _isSuppressed
                ? UpperBodyMode.Inactive
                : _requestedMode;

            if (!force && _currentMode == effectiveMode) return;

            _currentMode = effectiveMode;
            switch (effectiveMode)
            {
                case UpperBodyMode.RangedReady:
                    ChangeState(new RangedReadyState());
                    break;
                case UpperBodyMode.Inactive:
                default:
                    ChangeState(new InactiveState());
                    break;
            }

            _animationSink?.SetUpperBodyMode(effectiveMode);
        }

        /// <summary> 无上半身叠加的标记状态；为后续状态行为保留扩展点 </summary>
        private sealed class InactiveState : BaseCharacterState { }

        /// <summary> 远程武器持枪姿态标记状态；不承载攻击冷却或伤害逻辑 </summary>
        private sealed class RangedReadyState : BaseCharacterState { }
    }
}
