using Core;
using Dao;
using Model;
using System.Collections.Generic;
using UnityEngine;
using View;

namespace Controller
{
    public class CraftController
    {
        private RecipeDao _recipeDao;
        private IInventory _inventory;
        private Dictionary<int, RecipeInfo> _recipeDict;

        public void Init(IInventory inventory)
        {
            _recipeDao = new RecipeDao();
            _inventory = inventory;
            _recipeDict = _recipeDao.GetAllRecipes();
        }

        /// <summary>
        /// 获取所有配方
        /// </summary>
        public Dictionary<int, RecipeInfo> GetAllRecipes()
        {
            return _recipeDict;
        }

        /// <summary>
        /// 执行制作（原子操作由背包层保证：扣材料 + 加成品，内存与数据库一致提交）
        /// </summary>
        public bool DoCraft(int recipeId)
        {
            if (!_recipeDict.TryGetValue(recipeId, out var recipe))
            {
                Debug.LogError($"配方不存在 ID:{recipeId}");
                return false;
            }

            if (!_inventory.TryCraft(recipe, out string failReason))
            {
                Debug.LogWarning($"制作失败 配方ID:{recipeId} 原因:{failReason}");
                return false;
            }
            return true;
        }
    }
}