using Core;
using Dao;
using Model;
using System.Collections.Generic;
using UnityEngine;

namespace Controller
{
    public class BagController : IInventory
    {
        private BagDao _bagDao;
        private ItemDao _itemDao;
        private List<BagSlotInfo> _slotList; // 内存背包数据缓存
        private const int MAX_SLOT = 30;
        public int MaxSlot => MAX_SLOT;//公开

        public void Init()
        {
            _bagDao = new BagDao();
            _itemDao = new ItemDao();
            _slotList = _bagDao.LoadAllSlots(); // 启动时从数据库加载
        }

        /// <summary>
        /// 获取所有背包格子数据（给View刷新用）
        /// </summary>
        public List<BagSlotInfo> GetAllSlots()
        {
            return _slotList;
        }
        /// <summary>
        /// 获取背包已占有格子数量
        /// </summary>
        /// <returns></returns>
        public int GetUsedSlotCount()
        {
            int count = 0;
            foreach (var slot in _slotList)
            {
                if (slot.ItemId != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// 添加物品：自动堆叠、30格上限校验
        /// </summary>
        public bool AddItem(int itemId, int count)
        {
            if (count <= 0) return false;
            ItemInfo item = _itemDao.GetItemById(itemId);
            if (item == null)
            {
                Debug.LogError($"物品不存在 ID:{itemId}");
                return false;
            }

            int remain = count;
            int maxStack = item.MaxStack;

            // 1. 优先堆叠已有物品
            foreach (var slot in _slotList)
            {
                if (slot.ItemId == itemId && slot.ItemCount < maxStack)
                {
                    int canAdd = maxStack - slot.ItemCount;
                    int add = Mathf.Min(canAdd, remain);
                    slot.ItemCount += add;
                    remain -= add;
                    _bagDao.UpdateSlot(slot);

                    if (remain <= 0) break;
                }
            }

            // 2. 剩余放空格子
            if (remain > 0)
            {
                foreach (var slot in _slotList)
                {
                    if (slot.ItemId == 0)
                    {
                        int add = Mathf.Min(maxStack, remain);
                        slot.ItemId = itemId;
                        slot.ItemCount = add;
                        remain -= add;
                        _bagDao.UpdateSlot(slot);

                        if (remain <= 0) break;
                    }
                }
            }

            if (remain > 0)
            {
                Debug.LogWarning($"背包已满，剩余{remain}个无法放入");
                EventBus.Emit(EventName.Bag_Changed);
                return false;
            }

            EventBus.Emit(EventName.Bag_Changed);
            return true;
        }

        /// <summary>
        /// 移除物品
        /// </summary>
        public bool RemoveItem(int itemId, int count)
        {
            if (count <= 0) return false;
            int total = GetItemTotalCount(itemId);
            if (total < count)
            {
                Debug.LogWarning($"物品不足 ID:{itemId} 拥有:{total} 需要:{count}");
                return false;
            }

            int remain = count;
            foreach (var slot in _slotList)
            {
                if (slot.ItemId == itemId && slot.ItemCount > 0)
                {
                    int remove = Mathf.Min(slot.ItemCount, remain);
                    slot.ItemCount -= remove;
                    remain -= remove;

                    if (slot.ItemCount <= 0)
                    {
                        slot.ItemId = 0;
                        slot.ItemCount = 0;
                    }

                    _bagDao.UpdateSlot(slot);
                    if (remain <= 0) break;
                }
            }

            EventBus.Emit(EventName.Bag_Changed);
            return true;
        }

        /// <summary>
        /// 获取物品总数量
        /// </summary>
        public int GetItemTotalCount(int itemId)
        {
            int total = 0;
            foreach (var slot in _slotList)
            {
                if (slot.ItemId == itemId)
                    total += slot.ItemCount;
            }
            return total;
        }
    }
}