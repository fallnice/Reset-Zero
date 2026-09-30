using UnityEngine;

namespace Enemy.Influence
{
    /// <summary> 场景级敌人影响图配置，统一控制威胁、拥挤与刷新成本。 </summary>
    [CreateAssetMenu(fileName = "EnemyInfluenceMapConfig", menuName = "Enemy/Influence Map Config", order = 21)]
    public sealed class EnemyInfluenceMapConfig : ScriptableObject
    {
        [Header("刷新")]
        [Min(0.05f)] public float updateInterval = 0.25f;

        [Header("玩家威胁")]
        [Min(0f)] public float dangerRadius = 6f;
        [Min(0)] public int dangerCost = 18;

        [Header("敌人拥挤")]
        [Min(0f)] public float congestionRadius = 2.5f;
        [Min(0)] public int congestionCost = 24;

        [Header("成本")]
        [Min(1)] public int maxNodeCost = 64;

        [Header("调试")]
        public bool drawInfluence = true;
        [Min(1)] public int gizmoStride = 2;

        /// <summary> 在编辑器修改配置时约束参数，避免负代价破坏 A* 启发式前提。 </summary>
        private void OnValidate()
        {
            updateInterval = Mathf.Max(0.05f, updateInterval);
            dangerRadius = Mathf.Max(0f, dangerRadius);
            congestionRadius = Mathf.Max(0f, congestionRadius);
            dangerCost = Mathf.Max(0, dangerCost);
            congestionCost = Mathf.Max(0, congestionCost);
            maxNodeCost = Mathf.Max(1, maxNodeCost);
            gizmoStride = Mathf.Max(1, gizmoStride);
        }
    }
}
