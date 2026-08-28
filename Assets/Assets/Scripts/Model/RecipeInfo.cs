using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// 制作配方数据实体
    /// </summary>
    public class RecipeInfo
    {
        public int RecipeId;            // 配方ID
        public int ResultItemId;        // 成品物品ID
        public int ResultCount;         // 成品数量
        public Dictionary<int, int> Materials; // 材料ID:数量
    }
}