using Core;
using Model;
using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Dao
{
    public class ItemDao
    {
        /// <summary>
        /// 查询所有物品配置
        /// </summary>
        public Dictionary<int, ItemInfo> GetAllItems()
        {
            Dictionary<int, ItemInfo> dict = new Dictionary<int, ItemInfo>();
            string sql = "SELECT id, name, type, description, max_stack FROM ItemConfig";
            SqliteDataReader reader = SqliteManager.Instance.ExecuteQuery(sql);

            while (reader.Read())
            {
                ItemInfo info = new ItemInfo
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    Desc = reader.GetString(3),
                    MaxStack = reader.GetInt32(4)
                };
                dict.Add(info.Id, info);
            }
            reader.Close();
            return dict;
        }

        /// <summary>
        /// 根据ID查单个物品
        /// </summary>
        public ItemInfo GetItemById(int id)
        {
            string sql = $"SELECT id, name, type, description, max_stack FROM ItemConfig WHERE id = {id}";
            SqliteDataReader reader = SqliteManager.Instance.ExecuteQuery(sql);
            if (reader.Read())
            {
                ItemInfo info = new ItemInfo
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    Desc = reader.GetString(3),
                    MaxStack = reader.GetInt32(4)
                };
                reader.Close();
                return info;
            }
            reader.Close();
            return null;
        }
    }
}
