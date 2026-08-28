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
        /// 执行制作
        /// </summary>
        public bool DoCraft(int recipeId)
        {
            if (!_recipeDict.TryGetValue(recipeId, out var recipe))
            {
                Debug.LogError($"配方不存在 ID:{recipeId}");
                return false;
            }

            // 1. 校验材料
            foreach (var mat in recipe.Materials)
            {
                int have = _inventory.GetItemTotalCount(mat.Key);
                if (have < mat.Value)
                {
                    Debug.LogWarning($"材料不足 ID:{mat.Key} 拥有:{have} 需要:{mat.Value}");
                    return false;
                }
            }

            // 2. 事务保证原子性
            var transaction = SqliteManager.Instance.BeginTransaction();
            try
            {
                // 扣材料
                foreach (var mat in recipe.Materials)
                {
                    _inventory.RemoveItem(mat.Key, mat.Value);
                }

                // 加成品
                bool addSuccess = _inventory.AddItem(recipe.ResultItemId, recipe.ResultCount);
                if (!addSuccess)
                {
                    transaction.Rollback();
                    Debug.LogWarning("制作失败：背包空间不足");
                    return false;
                }

                transaction.Commit();
                Debug.Log($"制作成功，获得物品ID:{recipe.ResultItemId} 数量:{recipe.ResultCount}");
                return true;
            }
            catch (System.Exception e)
            {
                transaction.Rollback();
                Debug.LogError("制作异常：" + e.Message);
                return false;
            }
        }
    }
}