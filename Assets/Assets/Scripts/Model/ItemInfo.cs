namespace Model
{
    /// <summary>
    /// 物品配置数据实体
    /// </summary>
    public class ItemInfo
    {
        public int Id;          // 物品ID
        public string Name;     // 物品名称
        public string Type;     // 物品类型
        public string Desc;     // 物品描述
        public int MaxStack;    // 堆叠上限
    }
}