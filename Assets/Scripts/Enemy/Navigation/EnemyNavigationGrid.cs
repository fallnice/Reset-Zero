using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary>
    /// 场景级 2.5D 导航网格——在 XZ 平面采样单层地面高度与角色净空。
    /// 支持坡面/台阶，不支持同一 XZ 坐标存在多层可行走面的地图。
    /// </summary>
    public class EnemyNavigationGrid : MonoBehaviour
    {
        [Header("网格范围（以物体位置为中心，保持 Rotation=0 / Scale=1）")]
        [SerializeField] private Vector3 gridSize = new Vector3(30f, 8f, 30f);
        [Min(0.1f)] [SerializeField] private float cellSize = 0.5f;

        [Header("物理采样")]
        [SerializeField] private LayerMask walkableGroundMask;
        [SerializeField] private LayerMask navigationObstacleMask;
        [Min(0.05f)] [SerializeField] private float agentRadius = 0.4f;
        [Min(0.1f)] [SerializeField] private float agentHeight = 1.8f;
        [Min(0f)] [SerializeField] private float clearance = 0.05f;
        [Range(0f, 89f)] [SerializeField] private float maxSlope = 45f;
        [Min(0f)] [SerializeField] private float maxStepHeight = 0.3f;
        [Min(0)] [SerializeField] private int endpointSearchRadius = 3;

        [Header("调试")]
        [SerializeField] private bool drawGrid;
        [Min(1)] [SerializeField] private int gizmoStride = 1;

        private int _width;
        private int _depth;
        private Vector3 _origin;
        private Vector3[] _positions;
        private bool[] _walkable;
        private bool _isBuilt;
        private GridAStarPathfinder _pathfinder;

        public bool IsBuilt => _isBuilt;
        public int Width => _width;
        public int Depth => _depth;
        public int NodeCount => _width * _depth;
        public float CellSize => cellSize;
        public float MaxStepHeight => maxStepHeight;
        public int EndpointSearchRadius => endpointSearchRadius;
        public GridAStarPathfinder Pathfinder => _pathfinder;

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary> 首次查询前惰性构建；场景加载后只采样一次 </summary>
        public bool EnsureBuilt()
        {
            if (_isBuilt) return true;
            if (walkableGroundMask.value == 0)
            {
                Debug.LogError("[EnemyNavigationGrid] Walkable Ground Mask 为空，无法构建导航网格", this);
                return false;
            }
            if ((walkableGroundMask.value & navigationObstacleMask.value) != 0)
            {
                Debug.LogError("[EnemyNavigationGrid] Ground Mask 与 Obstacle Mask 重叠，会把地面误判为障碍", this);
                return false;
            }

            cellSize = Mathf.Max(0.1f, cellSize);
            _width = Mathf.Max(1, Mathf.CeilToInt(gridSize.x / cellSize));
            _depth = Mathf.Max(1, Mathf.CeilToInt(gridSize.z / cellSize));
            _origin = transform.position - new Vector3(_width * cellSize * 0.5f, 0f, _depth * cellSize * 0.5f);
            _positions = new Vector3[NodeCount];
            _walkable = new bool[NodeCount];

            // ProjectSettings 中 Auto Sync Transforms 关闭，首次物理采样前主动同步一次即可。
            Physics.SyncTransforms();
            for (int z = 0; z < _depth; z++)
            {
                for (int x = 0; x < _width; x++)
                    SampleNode(x, z);
            }

            _pathfinder = new GridAStarPathfinder(this);
            _isBuilt = true;
            return true;
        }

        private void SampleNode(int x, int z)
        {
            int index = GetIndex(x, z);
            Vector3 sample = _origin + new Vector3((x + 0.5f) * cellSize, gridSize.y * 0.5f, (z + 0.5f) * cellSize);
            float rayDistance = Mathf.Max(0.1f, gridSize.y);

            if (!Physics.Raycast(sample, Vector3.down, out RaycastHit hit, rayDistance,
                    walkableGroundMask, QueryTriggerInteraction.Ignore))
            {
                _positions[index] = new Vector3(sample.x, transform.position.y, sample.z);
                _walkable[index] = false;
                return;
            }

            _positions[index] = hit.point;
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlope)
            {
                _walkable[index] = false;
                return;
            }

            float radius = Mathf.Max(0.05f, agentRadius + clearance);
            float height = Mathf.Max(agentHeight, radius * 2f);
            Vector3 bottom = hit.point + Vector3.up * (radius + 0.02f);
            Vector3 top = hit.point + Vector3.up * (height - radius);
            bool blocked = navigationObstacleMask.value != 0
                && Physics.CheckCapsule(bottom, top, radius, navigationObstacleMask, QueryTriggerInteraction.Ignore);
            _walkable[index] = !blocked;
        }

        public int GetIndex(int x, int z) => z * _width + x;
        public int GetX(int index) => index % _width;
        public int GetZ(int index) => index / _width;
        public Vector3 GetNodePosition(int index) => _positions[index];
        public bool IsWalkable(int index) => index >= 0 && index < NodeCount && _walkable[index];

        public bool IsInside(int x, int z)
        {
            return x >= 0 && x < _width && z >= 0 && z < _depth;
        }

        public bool TryGetNodeFromWorld(Vector3 world, out int index)
        {
            int x = Mathf.FloorToInt((world.x - _origin.x) / cellSize);
            int z = Mathf.FloorToInt((world.z - _origin.z) / cellSize);
            if (!IsInside(x, z))
            {
                index = -1;
                return false;
            }

            index = GetIndex(x, z);
            return true;
        }

        /// <summary> 从所在格开始，在指定格数半径内找到最近可走节点 </summary>
        public bool TryFindNearestWalkable(Vector3 world, int searchRadius, out int result)
        {
            result = -1;
            if (!TryGetNodeFromWorld(world, out int center)) return false;

            int centerX = GetX(center);
            int centerZ = GetZ(center);
            float bestSqr = float.MaxValue;
            int radius = Mathf.Max(0, searchRadius);
            for (int z = centerZ - radius; z <= centerZ + radius; z++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (!IsInside(x, z)) continue;
                    int index = GetIndex(x, z);
                    if (!_walkable[index]) continue;

                    Vector3 delta = _positions[index] - world;
                    delta.y = 0f;
                    float sqr = delta.sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        result = index;
                    }
                }
            }
            return result >= 0;
        }

        /// <summary> 检查相邻节点间的台阶高度与对角穿角约束 </summary>
        public bool CanTraverse(int fromIndex, int toX, int toZ, int deltaX, int deltaZ)
        {
            if (!IsInside(toX, toZ)) return false;
            int toIndex = GetIndex(toX, toZ);
            if (!_walkable[toIndex]) return false;
            if (Mathf.Abs(_positions[toIndex].y - _positions[fromIndex].y) > maxStepHeight) return false;

            if (deltaX != 0 && deltaZ != 0)
            {
                int fromX = GetX(fromIndex);
                int fromZ = GetZ(fromIndex);
                int sideA = GetIndex(fromX + deltaX, fromZ);
                int sideB = GetIndex(fromX, fromZ + deltaZ);
                if (!_walkable[sideA] || !_walkable[sideB]) return false;
                if (Mathf.Abs(_positions[sideA].y - _positions[fromIndex].y) > maxStepHeight) return false;
                if (Mathf.Abs(_positions[sideB].y - _positions[fromIndex].y) > maxStepHeight) return false;
            }
            return true;
        }

        private void OnValidate()
        {
            gridSize.x = Mathf.Max(0.1f, gridSize.x);
            gridSize.y = Mathf.Max(0.1f, gridSize.y);
            gridSize.z = Mathf.Max(0.1f, gridSize.z);
            agentHeight = Mathf.Max(agentHeight, agentRadius * 2f);
            _isBuilt = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, gridSize);
            if (!drawGrid || !_isBuilt || _positions == null) return;

            int stride = Mathf.Max(1, gizmoStride);
            Vector3 size = new Vector3(cellSize * 0.8f, 0.03f, cellSize * 0.8f);
            for (int i = 0; i < NodeCount; i += stride)
            {
                Gizmos.color = _walkable[i] ? new Color(0f, 1f, 0.4f, 0.45f) : new Color(1f, 0f, 0f, 0.35f);
                Gizmos.DrawCube(_positions[i] + Vector3.up * 0.03f, size);
            }
        }
    }
}
