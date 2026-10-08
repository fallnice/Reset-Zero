using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Loot
{
    /// <summary> 敌人掉落表，每个条目独立计算概率并生成对应数量。 </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "Loot/Loot Table", order = 0)]
    public sealed class LootTable : ScriptableObject
    {
        [SerializeField] private List<LootEntry> entries = new List<LootEntry>();
        private ReadOnlyCollection<LootEntry> _readOnlyEntries;

        /// <summary> 只读暴露掉落条目，运行时不得修改配置集合。 </summary>
        public IReadOnlyList<LootEntry> Entries
        {
            get
            {
                if (entries == null)
                    entries = new List<LootEntry>();
                if (_readOnlyEntries == null)
                    _readOnlyEntries = entries.AsReadOnly();
                return _readOnlyEntries;
            }
        }

        /// <summary> 检查掉落表是否至少包含一个合法条目。 </summary>
        public bool HasValidEntry()
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].IsValid) return true;
            }
            return false;
        }

        /// <summary> 结算全部条目并把相同物品合并写入调用方复用的结果列表。 </summary>
        public void RollInto(List<LootDropResult> results)
        {
            if (results == null)
            {
                Debug.LogWarning("[LootTable] 掉落结果列表为空，无法结算", this);
                return;
            }

            results.Clear();
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                LootEntry entry = entries[i];
                if (entry == null || !entry.IsValid || entry.DropChance <= 0f) continue;
                if (entry.DropChance < 1f && Random.value >= entry.DropChance) continue;

                int count = Random.Range(entry.MinCount, entry.MaxCount + 1);
                MergeResult(results, entry.ItemId, count);
            }
        }

        /// <summary> 合并相同物品，避免生成多个内容完全相同的拾取物。 </summary>
        private static void MergeResult(List<LootDropResult> results, int itemId, int count)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].ItemId != itemId) continue;

                LootDropResult merged = results[i];
                long mergedCount = (long)merged.Count + count;
                merged.Count = mergedCount > int.MaxValue ? int.MaxValue : (int)mergedCount;
                results[i] = merged;
                return;
            }
            results.Add(new LootDropResult(itemId, count));
        }

        /// <summary> 在编辑器修改配置时修正非法条目参数。 </summary>
        private void OnValidate()
        {
            _readOnlyEntries = null;
        }
    }
}
