using System.Collections.Generic;
using Role;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 角色注册表——维护场景中活跃 CharacterRoot 的轻量列表，供感知与后续战术层查询。
    ///
    /// 目的：替换 EnemyPerception 里的 FindObjectsOfType（每次分配数组），
    /// 并为第四阶段的感知扩展（LOS、最后已知位置、威胁、掩体）提供统一、无分配的查询入口。
    ///
    /// 只保存角色引用，不保存运行时决策数据；决策数据仍放在各敌人的实例黑板/上下文中。
    /// 列表在角色 OnEnable/OnDisable 时维护，不依赖每帧扫描。
    /// </summary>
    public static class EnemyRegistry
    {
        private static readonly List<CharacterRoot> Roots = new List<CharacterRoot>();

        /// <summary> 当前注册的角色数量（调试用） </summary>
        public static int Count => Roots.Count;

        /// <summary> 注册角色；重复注册会被忽略 </summary>
        public static void Register(CharacterRoot root)
        {
            if (root == null || Roots.Contains(root)) return;
            Roots.Add(root);
        }

        /// <summary> 注销角色 </summary>
        public static void Unregister(CharacterRoot root)
        {
            if (root == null) return;
            Roots.Remove(root);
        }

        /// <summary>
        /// 查找范围内最近的存活玩家角色。
        /// 会顺带清理已销毁引用，避免角色被 Destroy 后残留空条目。
        /// </summary>
        public static bool TryGetNearestPlayer(
            Vector3 origin,
            float maxRange,
            CharacterRoot self,
            out CharacterRoot result,
            out float distance)
        {
            result = null;
            distance = 0f;

            float maxRangeSqr = maxRange * maxRange;
            float bestSqr = float.MaxValue;

            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                CharacterRoot candidate = Roots[i];
                if (candidate == null)
                {
                    Roots.RemoveAt(i);
                    continue;
                }
                if (candidate == self || !candidate.IsPlayerControlled) continue;
                if (candidate.Health != null && candidate.Health.IsDead) continue;

                float sqr = (candidate.transform.position - origin).sqrMagnitude;
                if (sqr > maxRangeSqr || sqr >= bestSqr) continue;

                bestSqr = sqr;
                result = candidate;
            }

            if (result == null) return false;

            distance = Mathf.Sqrt(bestSqr);
            return true;
        }
    }
}
