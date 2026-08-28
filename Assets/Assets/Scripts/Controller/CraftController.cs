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

            // 2. 预检背包空间：避免事务中途 AddItem 失败导致内存/数据库不一致
            if (!_inventory.CanAddItem(recipe.ResultItemId, recipe.ResultCount))
            {
                Debug.LogWarning("制作失败：背包空间不足");
                return false;
            }

            // 3. 事务保证原子性（SqliteManager 统一管理，DAO 写操作自动绑定当前事务）
            SqliteManager.Instance.BeginTransaction();
            try
            {
                // 扣材料
                foreach (var mat in recipe.Materials)
                {
                    _inventory.RemoveItem(mat.Key, mat.Value);
                }

                // 加成品（空间已预检，正常情况必成功；仍做防御检查）
                if (!_inventory.AddItem(recipe.ResultItemId, recipe.ResultCount))
                {
                    SqliteManager.Instance.RollbackTransaction();
                    Debug.LogWarning("制作失败：放入成品失败");
                    return false;
                }

                SqliteManager.Instance.CommitTransaction();
                return true;
            }
            catch (System.Exception e)
            {
                SqliteManager.Instance.RollbackTransaction();
                Debug.LogError("制作异常：" + e.Message);
                return false;
            }
        }
    }
}