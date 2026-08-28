using Core;
using Model;
using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Dao
{
    public class RecipeDao
    {
        /// <summary>
        /// 查询所有配方（联表查询材料）
        /// </summary>
        public Dictionary<int, RecipeInfo> GetAllRecipes()
        {
            Dictionary<int, RecipeInfo> dict = new Dictionary<int, RecipeInfo>();
            string sql = @"
                SELECT r.id, r.result_item_id, r.result_count, m.item_id, m.count
                FROM CraftMain r
                LEFT JOIN CraftSecond m ON r.id = m.Recipe_id
                ORDER BY r.id
            ";

            SqliteDataReader reader = SqliteManager.Instance.ExecuteQuery(sql);
            int lastId = -1;
            RecipeInfo current = null;

            while (reader.Read())
            {
                int recipeId = reader.GetInt32(0);
                if (recipeId != lastId)
                {
                    current = new RecipeInfo
                    {
                        RecipeId = recipeId,
                        ResultItemId = reader.GetInt32(1),
                        ResultCount = reader.GetInt32(2),
                        Materials = new Dictionary<int, int>()
                    };
                    dict.Add(recipeId, current);
                    lastId = recipeId;
                }

                int matId = reader.GetInt32(3);
                int matCount = reader.GetInt32(4);
                if (matId > 0 && current != null)
                {
                    current.Materials.Add(matId, matCount);
                }
            }
            reader.Close();
            return dict;
        }
    }
}