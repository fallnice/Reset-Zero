using Unity.Profiling;

namespace Enemy
{
    /// <summary> 集中声明敌人 AI 的 Unity Profiler 标记，便于多角色场景定位热点。 </summary>
    public static class EnemyProfilerMarkers
    {
        /// <summary> 单个 EnemyBrain 每帧决策与状态执行。 </summary>
        public static readonly ProfilerMarker BrainTick = new ProfilerMarker("Enemy.Brain.Tick");

        /// <summary> A* 路径重算，包括端点吸附和节点搜索。 </summary>
        public static readonly ProfilerMarker PathRebuild = new ProfilerMarker("Enemy.Navigation.Repath");

        /// <summary> Context Steering 邻居物理查询。 </summary>
        public static readonly ProfilerMarker AvoidanceNeighborQuery =
            new ProfilerMarker("Enemy.Avoidance.NeighborQuery");

        /// <summary> Context Steering 多方向障碍探测。 </summary>
        public static readonly ProfilerMarker AvoidanceObstacleQuery =
            new ProfilerMarker("Enemy.Avoidance.ObstacleQuery");

        /// <summary> Influence Map 低频动态成本快照刷新。 </summary>
        public static readonly ProfilerMarker InfluenceRefresh = new ProfilerMarker("Enemy.Influence.Refresh");
    }
}
