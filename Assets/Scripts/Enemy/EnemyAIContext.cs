using Enemy.Navigation;
using Role;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 敌人 AI 状态上下文——把角色、输入、导航、黑板与配置打包给具体状态。
    ///
    /// 由 EnemyBrain 持有并每帧刷新（不重新分配），状态类只读依赖、不反向持有 Brain，
    /// 便于后续 Utility / Influence Map 直接复用同一份上下文。
    /// </summary>
    public class EnemyAIContext
    {
        public CharacterRoot Character;
        public AIInputProvider AiInput;
        public IEnemyNavigation Navigation;
        public ILocalAvoidance LocalAvoidance;
        public EnemyAIBlackboard Blackboard;
        public EnemyConfig Config;
        public EnemyNavigationGrid Grid;
        public EnemyUtilityEvaluator Tactical;
        public Vector3 HomePosition;
        public float DeltaTime;
        public float NextAttackTime;

        /// <summary> 包抄冷却剩余时间；跨状态保留，避免切状态就能立刻再包抄 </summary>
        public float FlankCooldownRemaining;

        /// <summary> 撤退冷却剩余时间；跨状态保留，避免撤退一结束就立刻再撤 </summary>
        public float RetreatCooldownRemaining;

        /// <summary> 当前战术已持续的秒数；用于包抄超时兜底 </summary>
        public float TacticalElapsed;

        /// <summary>
        /// 从进入撤退开始累计的秒数。
        /// 不能复用 TacticalElapsed：后者以 tacticalCommitSeconds 为上限，
        /// 而撤退超时通常比它长，复用会导致超时永不触发。
        /// </summary>
        public float RetreatElapsed;

        /// <summary> 导航失败/卡住的累计时长；Patrol/ReturnHome 用它做兜底逃逸 </summary>
        public float NavigationFailedTime;

        // 本帧申请的迁移；由 EnemyBrain 在状态 OnUpdate 之后统一执行，避免在遍历中切状态
        private bool _hasPendingTransition;
        private EnemyAIState _pendingTransition;

        /// <summary> 本帧是否申请了状态迁移 </summary>
        public bool HasPendingTransition => _hasPendingTransition;

        /// <summary> 本帧申请迁移的目标状态 </summary>
        public EnemyAIState PendingTransition => _pendingTransition;

        /// <summary> 申请迁移到目标状态；同帧多次申请时以第一次为准 </summary>
        public void RequestTransition(EnemyAIState target)
        {
            if (_hasPendingTransition) return;

            _hasPendingTransition = true;
            _pendingTransition = target;
        }

        /// <summary> 每帧开始清空迁移申请与瞬时输入状态 </summary>
        public void BeginFrame()
        {
            _hasPendingTransition = false;
            _pendingTransition = default;
        }

        /// <summary> 复活/池化复用时重置瞬时计时，避免继承上一轮状态 </summary>
        public void ResetTimers()
        {
            NextAttackTime = 0f;
            NavigationFailedTime = 0f;
            FlankCooldownRemaining = 0f;
            RetreatCooldownRemaining = 0f;
            TacticalElapsed = 0f;
            RetreatElapsed = 0f;
            _hasPendingTransition = false;
            _pendingTransition = default;
        }

        /// <summary> 递减跨状态的冷却计时 </summary>
        public void TickCooldowns(float deltaTime)
        {
            if (FlankCooldownRemaining > 0f)
                FlankCooldownRemaining = Mathf.Max(0f, FlankCooldownRemaining - deltaTime);
            if (RetreatCooldownRemaining > 0f)
                RetreatCooldownRemaining = Mathf.Max(0f, RetreatCooldownRemaining - deltaTime);
        }

        /// <summary> 累计导航失败时长；导航正常时清零 </summary>
        public void UpdateNavigationFailure(float deltaTime)
        {
            if (Navigation == null)
            {
                NavigationFailedTime = 0f;
                return;
            }

            bool failing = Navigation.HasFailed || Navigation.IsStuck;
            NavigationFailedTime = failing ? NavigationFailedTime + deltaTime : 0f;
        }

        /// <summary> 统一停止导航、局部避障和移动输入，防止任一层保留上帧方向。 </summary>
        public void StopMovement()
        {
            Navigation?.Stop();
            LocalAvoidance?.Reset();
            AiInput?.SetMoveDirection(Vector3.zero);
        }

        /// <summary> 是否已离开出生点超过归位距离；0 表示不限制 </summary>
        public bool IsFarFromHome()
        {
            if (Character == null || Config == null) return false;
            if (Config.returnHomeDistance <= 0f) return false;

            Vector3 delta = Character.transform.position - HomePosition;
            delta.y = 0f;
            return delta.sqrMagnitude >= Config.returnHomeDistance * Config.returnHomeDistance;
        }
    }
}
