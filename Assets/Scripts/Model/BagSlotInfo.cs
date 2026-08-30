namespace Model
{
    /// <summary>
    /// 背包格子数据实体
    /// </summary>
    public class BagSlotInfo
    {
        public int SlotId;      // 格子编号 1~30
        public int ItemId;      // 物品ID，0为空
        public int ItemCount;   // 物品数量
    }
}