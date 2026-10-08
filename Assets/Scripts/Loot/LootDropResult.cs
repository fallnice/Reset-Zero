namespace Loot
{
    /// <summary> 一次掉落结算产生的物品 ID 与合并后数量。 </summary>
    public struct LootDropResult
    {
        public int ItemId;
        public int Count;

        /// <summary> 创建一项已结算的掉落结果。 </summary>
        public LootDropResult(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }
}
