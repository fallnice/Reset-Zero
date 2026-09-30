using System.Collections.Generic;
using Combat;
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
        private static long _nextAlertSequence;

        /// <summary> 最近一次报警成功投递的接收者数量，供无头验收和运行诊断读取。 </summary>
        public static int LastAlertRecipientCount { get; private set; }
        /// <summary> 当前运行期已创建的报警总数。 </summary>
        public static long AlertSequence => _nextAlertSequence;

        /// <summary> 每次进入运行期前清空静态注册表，兼容关闭 Domain Reload 的编辑器设置。 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Roots.Clear();
            _nextAlertSequence = 0;
            LastAlertRecipientCount = 0;
        }

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

        /// <summary> 按索引读取活跃角色，不暴露可变列表且不创建枚举器或数组。 </summary>
        public static bool TryGetAt(int index, out CharacterRoot root)
        {
            root = null;
            if (index < 0 || index >= Roots.Count) return false;

            CharacterRoot candidate = Roots[index];
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                return false;

            root = candidate;
            return true;
        }

        /// <summary>
        /// 向所有角色广播一次武器声。
        /// 战斗层还没有带位置的开火事件，只能这样近似；每个敌人的 EnemyHearing 会自行按距离过滤，
        /// 并忽略自己发出的声音。低频调用，线性遍历可接受。
        /// </summary>
        public static void BroadcastWeaponNoise(CharacterRoot source, Vector3 sourcePosition)
        {
            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                CharacterRoot root = Roots[i];
                if (root == null)
                {
                    Roots.RemoveAt(i);
                    continue;
                }
                if (!root.isActiveAndEnabled || !root.gameObject.activeInHierarchy) continue;
                root.Brain?.NotifyHeardWeaponNoise(source, sourcePosition);
            }
        }

        /// <summary>
        /// 把发送者亲眼确认的目标位置单跳投递给范围内同阵营敌人；接收者不会再次转发。
        /// </summary>
        public static int BroadcastGroupAlert(
            CharacterRoot sender,
            CharacterRoot target,
            Vector3 confirmedPosition,
            float range,
            float lifetimeSeconds,
            float suspicionBoost)
        {
            LastAlertRecipientCount = 0;
            if (sender == null || target == null || range <= 0f || lifetimeSeconds <= 0f) return 0;
            if (sender.Faction == Faction.Neutral || sender.Faction == target.Faction) return 0;

            _nextAlertSequence++;
            EnemyAlert alert = new EnemyAlert(
                _nextAlertSequence,
                sender,
                target,
                confirmedPosition,
                sender.Faction,
                Time.time + lifetimeSeconds,
                suspicionBoost);
            float rangeSqr = range * range;

            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                CharacterRoot root = Roots[i];
                if (root == null)
                {
                    Roots.RemoveAt(i);
                    continue;
                }
                if (root == sender || root.Faction != sender.Faction) continue;
                if (!root.isActiveAndEnabled || !root.gameObject.activeInHierarchy) continue;
                if (root.Health != null && root.Health.IsDead) continue;

                Vector3 delta = root.transform.position - sender.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > rangeSqr) continue;
                if (root.Brain == null || !root.Brain.ReceiveGroupAlert(alert)) continue;

                LastAlertRecipientCount++;
            }

            return LastAlertRecipientCount;
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
                if (!candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy) continue;
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
