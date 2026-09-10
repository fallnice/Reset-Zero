using UnityEngine;

namespace Enemy.Navigation
{
    /// <summary>
    /// 网格 A* 查询器——复用扁平数组与索引二叉堆，单线程主循环下重复查询不产生托管分配。
    /// 由场景级 EnemyNavigationGrid 共享，输出写入调用方提供的固定缓冲区。
    /// </summary>
    public sealed class GridAStarPathfinder
    {
        private readonly EnemyNavigationGrid _grid;
        private readonly int[] _gCost;
        private readonly int[] _parent;
        private readonly byte[] _state; // 0=未访问，1=Open，2=Closed
        private readonly int[] _heap;
        private readonly int[] _heapPosition;
        private int _heapCount;
        private int _goalIndex;

        private static readonly int[] NeighborX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] NeighborZ = { -1, -1, -1, 0, 0, 1, 1, 1 };

        public GridAStarPathfinder(EnemyNavigationGrid grid)
        {
            _grid = grid;
            int count = grid.NodeCount;
            _gCost = new int[count];
            _parent = new int[count];
            _state = new byte[count];
            _heap = new int[count];
            _heapPosition = new int[count];
        }

        public bool TryFindPath(
            Vector3 startWorld,
            Vector3 destinationWorld,
            int maxExpandedNodes,
            int[] outputPath,
            out int outputCount,
            out EnemyNavigationFailure failure)
        {
            outputCount = 0;
            failure = EnemyNavigationFailure.None;
            if (_grid == null || !_grid.IsBuilt)
            {
                failure = EnemyNavigationFailure.GridUnavailable;
                return false;
            }
            if (!_grid.TryGetNodeFromWorld(startWorld, out _))
            {
                failure = EnemyNavigationFailure.StartOutsideGrid;
                return false;
            }
            if (!_grid.TryGetNodeFromWorld(destinationWorld, out _))
            {
                failure = EnemyNavigationFailure.DestinationOutsideGrid;
                return false;
            }
            if (!_grid.TryFindNearestWalkable(startWorld, _grid.EndpointSearchRadius, out int startIndex))
            {
                failure = EnemyNavigationFailure.StartNotWalkable;
                return false;
            }
            if (!_grid.TryFindNearestWalkable(destinationWorld, _grid.EndpointSearchRadius, out int goalIndex))
            {
                failure = EnemyNavigationFailure.DestinationNotWalkable;
                return false;
            }

            ResetSearchBuffers();
            _goalIndex = goalIndex;
            _gCost[startIndex] = 0;
            _parent[startIndex] = -1;
            AddToHeap(startIndex);
            _state[startIndex] = 1;

            int expanded = 0;
            int searchLimit = maxExpandedNodes <= 0
                ? _grid.NodeCount
                : Mathf.Min(maxExpandedNodes, _grid.NodeCount);

            while (_heapCount > 0)
            {
                int current = RemoveHeapRoot();
                if (_state[current] == 2) continue;
                _state[current] = 2;

                if (current == goalIndex)
                    return ReconstructPath(startIndex, goalIndex, outputPath, out outputCount, out failure);

                if (expanded >= searchLimit)
                {
                    failure = EnemyNavigationFailure.SearchLimitReached;
                    return false;
                }
                expanded++;

                int currentX = _grid.GetX(current);
                int currentZ = _grid.GetZ(current);
                for (int i = 0; i < NeighborX.Length; i++)
                {
                    int dx = NeighborX[i];
                    int dz = NeighborZ[i];
                    int nx = currentX + dx;
                    int nz = currentZ + dz;
                    if (!_grid.CanTraverse(current, nx, nz, dx, dz)) continue;

                    int neighbor = _grid.GetIndex(nx, nz);
                    if (_state[neighbor] == 2) continue;

                    int stepCost = dx != 0 && dz != 0 ? 14 : 10;
                    int tentative = _gCost[current] + stepCost;
                    if (_state[neighbor] != 1 || tentative < _gCost[neighbor])
                    {
                        _gCost[neighbor] = tentative;
                        _parent[neighbor] = current;
                        if (_state[neighbor] != 1)
                        {
                            _state[neighbor] = 1;
                            AddToHeap(neighbor);
                        }
                        else
                        {
                            UpdateHeap(neighbor);
                        }
                    }
                }
            }

            failure = EnemyNavigationFailure.NoPath;
            return false;
        }

        private void ResetSearchBuffers()
        {
            _heapCount = 0;
            for (int i = 0; i < _grid.NodeCount; i++)
            {
                _gCost[i] = int.MaxValue;
                _parent[i] = -1;
                _state[i] = 0;
                _heapPosition[i] = -1;
            }
        }

        private bool ReconstructPath(
            int startIndex,
            int goalIndex,
            int[] outputPath,
            out int outputCount,
            out EnemyNavigationFailure failure)
        {
            outputCount = 0;
            failure = EnemyNavigationFailure.None;
            int current = goalIndex;
            while (current >= 0)
            {
                if (outputCount >= outputPath.Length)
                {
                    outputCount = 0;
                    failure = EnemyNavigationFailure.SearchLimitReached;
                    return false;
                }
                outputPath[outputCount++] = current;
                if (current == startIndex) break;
                current = _parent[current];
            }

            if (current != startIndex)
            {
                outputCount = 0;
                failure = EnemyNavigationFailure.NoPath;
                return false;
            }

            for (int left = 0, right = outputCount - 1; left < right; left++, right--)
            {
                int temp = outputPath[left];
                outputPath[left] = outputPath[right];
                outputPath[right] = temp;
            }

            CompressCollinear(outputPath, ref outputCount);
            return true;
        }

        /// <summary> 删除方向不变的中间节点，减少跟随点数量且不会切墙角 </summary>
        private void CompressCollinear(int[] path, ref int count)
        {
            if (count <= 2) return;

            int write = 1;
            int previousDirectionX = _grid.GetX(path[1]) - _grid.GetX(path[0]);
            int previousDirectionZ = _grid.GetZ(path[1]) - _grid.GetZ(path[0]);
            for (int read = 2; read < count; read++)
            {
                int directionX = _grid.GetX(path[read]) - _grid.GetX(path[read - 1]);
                int directionZ = _grid.GetZ(path[read]) - _grid.GetZ(path[read - 1]);
                if (directionX != previousDirectionX || directionZ != previousDirectionZ)
                {
                    path[write++] = path[read - 1];
                    previousDirectionX = directionX;
                    previousDirectionZ = directionZ;
                }
            }
            path[write++] = path[count - 1];
            count = write;
        }

        private int Heuristic(int node)
        {
            int dx = Mathf.Abs(_grid.GetX(node) - _grid.GetX(_goalIndex));
            int dz = Mathf.Abs(_grid.GetZ(node) - _grid.GetZ(_goalIndex));
            int diagonal = Mathf.Min(dx, dz);
            int straight = Mathf.Max(dx, dz) - diagonal;
            return diagonal * 14 + straight * 10;
        }

        private bool HasHigherPriority(int a, int b)
        {
            int aH = Heuristic(a);
            int bH = Heuristic(b);
            int aF = _gCost[a] + aH;
            int bF = _gCost[b] + bH;
            return aF < bF || (aF == bF && aH < bH);
        }

        private void AddToHeap(int node)
        {
            int position = _heapCount++;
            _heap[position] = node;
            _heapPosition[node] = position;
            BubbleUp(position);
        }

        private int RemoveHeapRoot()
        {
            int root = _heap[0];
            _heapPosition[root] = -1;
            _heapCount--;
            if (_heapCount > 0)
            {
                int moved = _heap[_heapCount];
                _heap[0] = moved;
                _heapPosition[moved] = 0;
                BubbleDown(0);
            }
            return root;
        }

        private void UpdateHeap(int node)
        {
            int position = _heapPosition[node];
            if (position < 0) return;
            BubbleUp(position);
            BubbleDown(_heapPosition[node]);
        }

        private void BubbleUp(int position)
        {
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                if (!HasHigherPriority(_heap[position], _heap[parent])) break;
                Swap(position, parent);
                position = parent;
            }
        }

        private void BubbleDown(int position)
        {
            while (true)
            {
                int left = position * 2 + 1;
                if (left >= _heapCount) return;
                int right = left + 1;
                int best = right < _heapCount && HasHigherPriority(_heap[right], _heap[left]) ? right : left;
                if (!HasHigherPriority(_heap[best], _heap[position])) return;
                Swap(position, best);
                position = best;
            }
        }

        private void Swap(int a, int b)
        {
            int temp = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = temp;
            _heapPosition[_heap[a]] = a;
            _heapPosition[_heap[b]] = b;
        }
    }
}
