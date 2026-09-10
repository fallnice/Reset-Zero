using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary> 导航运行状态 </summary>
    public enum EnemyNavigationStatus
    {
        Idle,
        Moving,
        Reached,
        Failed,
        Stuck
    }

    /// <summary> 导航失败原因，用于诊断配置或运行时问题 </summary>
    public enum EnemyNavigationFailure
    {
        None,
        GridUnavailable,
        StartOutsideGrid,
        DestinationOutsideGrid,
        StartNotWalkable,
        DestinationNotWalkable,
        SearchLimitReached,
        NoPath,
        NoProgress
    }

    /// <summary>
    /// 敌人导航抽象——只产生世界空间移动意图，不直接移动 Transform/CharacterController。
    /// EnemyBrain 每帧按 SetDestination → Tick → MoveDirection 的顺序主动驱动。
    /// </summary>
    public interface IEnemyNavigation
    {
        Vector3 MoveDirection { get; }
        Vector3 Destination { get; }
        EnemyNavigationStatus Status { get; }
        EnemyNavigationFailure LastFailure { get; }
        bool HasReachedDestination { get; }
        bool HasFailed { get; }
        bool IsStuck { get; }

        void Initialize(Transform agent, CharacterController controller, EnemyConfig config);
        void SetDestination(Vector3 destination);
        void Tick(float deltaTime);
        void Stop();
    }
}
