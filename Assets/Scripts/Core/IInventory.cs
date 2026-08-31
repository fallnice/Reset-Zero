using Model;

namespace Core
{
    /// <summary>
    /// 背包系统抽象接口
    /// 单机：BagController 直接实现
    /// 联机：客户端 BagProxy（转发网络请求）、服务端 BagAuthority（操作数据库）
    /// </summary>
    public interface IInventory
    {
        /// <summary>添加物品，返回是否成功</summary>
        bool AddItem(int itemId, int count);

        /// <summary>移除物品，返回是否成功</summary>
        bool RemoveItem(int itemId, int count);

        /// <summary>预检能否放入指定数量的物品（不实际修改背包）</summary>
        bool CanAddItem(int itemId, int count);

        /// <summary>获取物品总持有数量</summary>
        int GetItemTotalCount(int itemId);

        /// <summary>原子制作：扣材料 + 加成品，内存与数据库一致提交；失败时两者都不变</summary>
        bool TryCraft(RecipeInfo recipe, out string failReason);
    }
}
