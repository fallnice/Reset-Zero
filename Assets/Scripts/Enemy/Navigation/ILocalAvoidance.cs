using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary> 局部避障运行状态，供诊断与 Gizmos 使用。 </summary>
    public enum LocalAvoidanceStatus
    {
        Disabled,
        Clear,
        Adjusting,
        Blocked
    }

    /// <summary>
    /// 局部避障只修正导航给出的期望方向，不负责寻路或直接移动角色。
    /// </summary>
    public interface ILocalAvoidance
    {
        /// <summary> 导航层提供的原始水平期望方向。 </summary>
        Vector3 RawDirection { get; }
        /// <summary> 局部避障修正后的最终水平方向。 </summary>
        Vector3 AdjustedDirection { get; }
        /// <summary> 最近一次查询识别到的有效邻居数。 </summary>
        int NeighborCount { get; }
        /// <summary> 最近一次物理查询是否填满固定缓冲区。 </summary>
        bool IsNeighborBufferSaturated { get; }
        /// <summary> 当前局部避障运行状态。 </summary>
        LocalAvoidanceStatus Status { get; }

        /// <summary> 注入角色、控制器和配置依赖。 </summary>
        void Initialize(Transform agent, CharacterController controller, EnemyConfig config);
        /// <summary> 根据附近障碍和角色修正本帧期望方向。 </summary>
        Vector3 AdjustDirection(Vector3 desiredDirection, float deltaTime);
        /// <summary> 清空方向、邻居和诊断状态。 </summary>
        void Reset();
    }
}
