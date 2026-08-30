using Core;
using Model;
using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Dao
{
    public class BagDao
    {
        /// <summary>
        /// 加载所有背包格子数据
        /// </summary>
        public List<BagSlotInfo> LoadAllSlots()
        {
            List<BagSlotInfo> list = new List<BagSlotInfo>();
            string sql = "SELECT slot_id, item_id, item_count FROM PlayerBag ORDER BY slot_id";
            SqliteDataReader reader = SqliteManager.Instance.ExecuteQuery(sql);

            while (reader.Read())
            {
                BagSlotInfo slot = new BagSlotInfo
                {
                    SlotId = reader.GetInt32(0),
                    ItemId = reader.GetInt32(1),
                    ItemCount = reader.GetInt32(2)
                };
                list.Add(slot);
            }
            reader.Close();
            return list;
        }

        /// <summary>
        /// 更新单个格子数据到数据库
        /// </summary>
        public void UpdateSlot(BagSlotInfo slot)
        {
            string sql = $"UPDATE PlayerBag SET item_id = {slot.ItemId}, item_count = {slot.ItemCount} WHERE slot_id = {slot.SlotId}";
            SqliteManager.Instance.ExecuteNonQuery(sql);
        }
    }
}