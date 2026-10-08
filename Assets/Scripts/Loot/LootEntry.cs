using System;
using UnityEngine;

namespace Loot
{
    /// <summary> 单项掉落配置，定义物品、数量范围和独立掉落概率。 </summary>
    [Serializable]
    public sealed class LootEntry : ISerializationCallbackReceiver
    {
        [Min(1)] [SerializeField] private int itemId = 1;
        [Min(1)] [SerializeField] private int minCount = 1;
        [Min(1)] [SerializeField] private int maxCount = 1;
        [Range(0f, 1f)] [SerializeField] private float dropChance = 1f;

        /// <summary> 掉落物品 ID。 </summary>
        public int ItemId => itemId;

        /// <summary> 最小掉落数量。 </summary>
        public int MinCount => minCount;

        /// <summary> 最大掉落数量。 </summary>
        public int MaxCount => maxCount;

        /// <summary> 独立掉落概率，范围为 0 到 1。 </summary>
        public float DropChance => dropChance;

        /// <summary> 当前条目是否具备可结算的基础参数。 </summary>
        public bool IsValid => itemId > 0 && minCount > 0 && maxCount >= minCount
            && maxCount < int.MaxValue && dropChance >= 0f && dropChance <= 1f;

        /// <summary> 创建使用默认值的序列化条目。 </summary>
        public LootEntry()
        {
        }

        /// <summary> 创建指定参数的只读掉落条目，主要用于自动验收。 </summary>
        public LootEntry(int newItemId, int newMinCount, int newMaxCount, float newDropChance)
        {
            itemId = newItemId;
            minCount = newMinCount;
            maxCount = newMaxCount;
            dropChance = newDropChance;
            ClampSerializedValues();
        }

        /// <summary> 序列化前无需修改只读配置。 </summary>
        public void OnBeforeSerialize()
        {
        }

        /// <summary> 反序列化后修正非法参数，避免运行时暴露配置写入口。 </summary>
        public void OnAfterDeserialize()
        {
            ClampSerializedValues();
        }

        /// <summary> 约束序列化字段范围。 </summary>
        private void ClampSerializedValues()
        {
            itemId = Mathf.Max(1, itemId);
            minCount = Mathf.Max(1, minCount);
            maxCount = Mathf.Clamp(maxCount, minCount, int.MaxValue - 1);
            dropChance = Mathf.Clamp01(dropChance);
        }
    }
}
