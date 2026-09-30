using Combat;
using Enemy.Navigation;
using Role;
using UnityEngine;

namespace Enemy.Influence
{
    /// <summary>
    /// 场景级动态影响图——与 EnemyNavigationGrid 节点一一对齐，低频聚合玩家威胁和敌人拥挤成本。
    /// A* 与 Utility 只读取已发布快照，刷新过程复用数组，不产生每帧托管分配。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(EnemyNavigationGrid))]
    public sealed class EnemyInfluenceMap : MonoBehaviour
    {
        [SerializeField] private EnemyInfluenceMapConfig config;

        private EnemyNavigationGrid _grid;
        private int[] _costs;
        private int[] _nextCosts;
        private float _nextUpdateTime;
        private bool _isReady;

        /// <summary> 当前配置；供场景审计检查必填引用。 </summary>
        public EnemyInfluenceMapConfig Configuration => config;

        /// <summary> 影响图已构建且与当前导航网格尺寸一致。 </summary>
        public bool IsReady => isActiveAndEnabled && _isReady && _grid != null && _costs != null
            && _costs.Length == _grid.NodeCount;

        /// <summary> 已发布快照版本；仅成本变化时递增。 </summary>
        public int Version { get; private set; }

        /// <summary> 最近一次刷新采集到的活跃玩家源数量。 </summary>
        public int PlayerSourceCount { get; private set; }

        /// <summary> 最近一次刷新采集到的活跃敌人源数量。 </summary>
        public int EnemySourceCount { get; private set; }

        private void Awake()
        {
            if (!TryGetComponent(out _grid))
            {
                Debug.LogWarning("[EnemyInfluenceMap] 缺少 EnemyNavigationGrid，影响图已停用", this);
                enabled = false;
                return;
            }

            TryInitialize();
        }

        private void OnEnable()
        {
            // 延后一帧采样，确保所有 CharacterRoot.OnEnable 已完成注册。
            _nextUpdateTime = Time.time + Mathf.Epsilon;
        }

        private void OnDisable()
        {
            // 版本变化让现有导航在节流窗口后重算为无动态成本路径。
            Version++;
        }

        private void Update()
        {
            if (!TryInitialize() || Time.time < _nextUpdateTime) return;

            Refresh();
            _nextUpdateTime = Time.time + config.updateInterval;
        }

        /// <summary> 返回指定节点的非负动态代价；未就绪或索引非法时安全返回 0。 </summary>
        public int GetCost(int nodeIndex)
        {
            if (!IsReady || nodeIndex < 0 || nodeIndex >= _costs.Length) return 0;
            return _costs[nodeIndex];
        }

        /// <summary> 将世界坐标吸附到邻近可走节点并采样影响代价。 </summary>
        public bool TrySample(Vector3 worldPosition, out int cost)
        {
            cost = 0;
            if (!IsReady || !_grid.TryFindNearestWalkable(
                    worldPosition, _grid.EndpointSearchRadius, out int nodeIndex))
            {
                return false;
            }

            cost = _costs[nodeIndex];
            return true;
        }

        /// <summary> 延迟初始化数组，兼容组件 Awake 顺序及运行期网格重建。 </summary>
        private bool TryInitialize()
        {
            if (config == null)
            {
                _isReady = false;
                return false;
            }
            if (_grid == null && !TryGetComponent(out _grid))
            {
                _isReady = false;
                return false;
            }
            if (!_grid.EnsureBuilt() || _grid.NodeCount <= 0)
            {
                _isReady = false;
                return false;
            }
            if (_costs != null && _costs.Length == _grid.NodeCount)
            {
                _isReady = true;
                return true;
            }

            _costs = new int[_grid.NodeCount];
            _nextCosts = new int[_grid.NodeCount];
            _isReady = true;
            Version++;
            return true;
        }

        /// <summary> 低频重建下一份快照，变化后原子交换数组供所有敌人读取。 </summary>
        private void Refresh()
        {
            System.Array.Clear(_nextCosts, 0, _nextCosts.Length);
            PlayerSourceCount = 0;
            EnemySourceCount = 0;

            for (int i = 0; i < EnemyRegistry.Count; i++)
            {
                if (!EnemyRegistry.TryGetAt(i, out CharacterRoot root)) continue;
                if (root.Health != null && root.Health.IsDead) continue;

                if (root.Faction == Faction.Player)
                {
                    PlayerSourceCount++;
                    AddRadialCost(root.transform.position, config.dangerRadius, config.dangerCost);
                }
                else if (root.Faction == Faction.Enemy)
                {
                    EnemySourceCount++;
                    AddRadialCost(root.transform.position, config.congestionRadius, config.congestionCost);
                }
            }

            bool changed = false;
            for (int i = 0; i < _nextCosts.Length; i++)
            {
                int clamped = Mathf.Min(_nextCosts[i], config.maxNodeCost);
                _nextCosts[i] = clamped;
                if (clamped != _costs[i]) changed = true;
            }
            if (!changed) return;

            int[] previous = _costs;
            _costs = _nextCosts;
            _nextCosts = previous;
            Version++;
        }

        /// <summary> 按水平距离线性衰减叠加一个影响源；节点数组固定复用。 </summary>
        private void AddRadialCost(Vector3 sourcePosition, float radius, int peakCost)
        {
            if (radius <= 0f || peakCost <= 0) return;
            if (!_grid.TryGetNodeFromWorld(sourcePosition, out int centerIndex)) return;

            float radiusSqr = radius * radius;
            int cellRadius = Mathf.CeilToInt(radius / _grid.CellSize);
            int centerX = _grid.GetX(centerIndex);
            int centerZ = _grid.GetZ(centerIndex);
            int minX = Mathf.Max(0, centerX - cellRadius);
            int maxX = Mathf.Min(_grid.Width - 1, centerX + cellRadius);
            int minZ = Mathf.Max(0, centerZ - cellRadius);
            int maxZ = Mathf.Min(_grid.Depth - 1, centerZ + cellRadius);

            // 只扫描影响半径包围盒，成本随地图面积增长而非“源数量 × 全网格节点数”增长。
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = _grid.GetIndex(x, z);
                    if (!_grid.IsWalkable(index)) continue;

                    Vector3 node = _grid.GetNodePosition(index);
                    float deltaX = node.x - sourcePosition.x;
                    float deltaZ = node.z - sourcePosition.z;
                    float sqrDistance = deltaX * deltaX + deltaZ * deltaZ;
                    if (sqrDistance >= radiusSqr) continue;

                    float normalized = 1f - Mathf.Sqrt(sqrDistance) / radius;
                    _nextCosts[index] += Mathf.CeilToInt(peakCost * normalized);
                }
            }
        }

        /// <summary> 选中影响图时以绿到红显示当前动态成本快照。 </summary>
        private void OnDrawGizmosSelected()
        {
            if (config == null || !config.drawInfluence || !IsReady) return;

            int stride = Mathf.Max(1, config.gizmoStride);
            float maxCost = Mathf.Max(1, config.maxNodeCost);
            Vector3 size = new Vector3(_grid.CellSize * 0.72f, 0.04f, _grid.CellSize * 0.72f);
            for (int i = 0; i < _grid.NodeCount; i += stride)
            {
                if (!_grid.IsWalkable(i) || _costs[i] <= 0) continue;

                float ratio = Mathf.Clamp01(_costs[i] / maxCost);
                Gizmos.color = Color.Lerp(new Color(0f, 0.8f, 0.2f, 0.2f), new Color(1f, 0f, 0f, 0.65f), ratio);
                Gizmos.DrawCube(_grid.GetNodePosition(i) + Vector3.up * 0.08f, size);
            }
        }
    }
}
