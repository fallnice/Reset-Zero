using System.Collections.Generic;
using Core;
using Dao;
using Model;
using UnityEngine;

namespace Controller
{
    /// <summary> 制造逻辑控制器：加载配方并通过 IInventory 原子执行制造。 </summary>
    public class CraftController
    {
        private const string REASON_NOT_INITIALIZED = "制造系统未初始化";
        private const string REASON_INVALID_RECIPE = "配方 ID 非法";
        private const string REASON_RECIPE_NOT_FOUND = "配方不存在";

        private RecipeDao _recipeDao;
        private IInventory _inventory;
        private Dictionary<int, RecipeInfo> _recipeDict;
        private readonly Dictionary<int, RecipeInfo> _emptyRecipes = new Dictionary<int, RecipeInfo>();

        /// <summary> 注入库存并加载全部配方。 </summary>
        public void Init(IInventory inventory)
        {
            if (inventory == null)
            {
                Debug.LogWarning("[CraftController] Inventory 为空，制造系统无法初始化");
                return;
            }

            _recipeDao = new RecipeDao();
            _inventory = inventory;
            _recipeDict = _recipeDao.GetAllRecipes();
            if (_recipeDict == null)
            {
                Debug.LogWarning("[CraftController] 配方数据读取失败，使用空配方表");
                _recipeDict = new Dictionary<int, RecipeInfo>();
            }
        }

        /// <summary> 获取只读用途的当前配方表；未初始化时返回空字典。 </summary>
        public Dictionary<int, RecipeInfo> GetAllRecipes()
        {
            return _recipeDict ?? _emptyRecipes;
        }

        /// <summary> 执行制造；背包层保证材料与成品在同一事务中提交。 </summary>
        public bool DoCraft(int recipeId)
        {
            if (_inventory == null || _recipeDict == null)
                return FailCraft(recipeId, REASON_NOT_INITIALIZED);
            if (recipeId <= 0)
                return FailCraft(recipeId, REASON_INVALID_RECIPE);
            if (!_recipeDict.TryGetValue(recipeId, out RecipeInfo recipe) || recipe == null)
                return FailCraft(recipeId, REASON_RECIPE_NOT_FOUND);

            if (!_inventory.TryCraft(recipe, out string failReason))
                return FailCraft(recipeId, failReason ?? "未知原因");

            EventBus.Emit(EventName.Craft_Success, recipe.RecipeId, recipe.ResultItemId, recipe.ResultCount);
            return true;
        }

        /// <summary> 统一制造失败出口，保证每次失败只发布一次带原因的事件。 </summary>
        private static bool FailCraft(int recipeId, string reason)
        {
            Debug.LogWarning($"制作失败 配方ID:{recipeId} 原因:{reason}");
            EventBus.Emit(EventName.Craft_Fail, recipeId, reason);
            return false;
        }
    }
}
