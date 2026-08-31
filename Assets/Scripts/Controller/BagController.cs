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
        /// 添加物品：先在副本上预演（自动堆叠、30格上限），成功后才事务写库并原地更新内存，
        /// 避免空间不足时「改了一半内存/库」的部分写入问题。
        /// </summary>
        public bool AddItem(int itemId, int count)
        {
            if (count <= 0) return false;

            var snapshot = CloneSlots();
            if (!ApplyAddTo(snapshot, itemId, count, out string reason))
            {
                Debug.LogWarning($"添加物品失败 ID:{itemId} 数量:{count} 原因:{reason}");
                return false;
            }

            try
            {
                SqliteManager.Instance.RunInTransaction(() =>
                {
                    foreach (var slot in snapshot)
                    {
                        _bagDao.UpdateSlot(slot);
                    }
                });
            }
            catch (System.Exception e)
            {
                Debug.LogError("添加物品写库失败：" + e.Message);
                return false;
            }

            ApplySnapshotToLive(snapshot);
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
        /// 预检能否放入指定数量的物品（不实际修改背包）
        /// 供调用方在批量操作前判断空间是否足够
        /// </summary>
        public bool CanAddItem(int itemId, int count)
        {
            if (count <= 0) return false;
            ItemInfo item = _itemDao.GetItemById(itemId);
            if (item == null)
            {
                Debug.LogError($"物品不存在 ID:{itemId}");
                return false;
            }

            int capacity = 0;
            int maxStack = item.MaxStack;
            foreach (var slot in _slotList)
            {
                if (slot.ItemId == itemId)
                {
                    capacity += maxStack - slot.ItemCount; // 已有同类堆叠剩余空间
                }
                else if (slot.ItemId == 0)
                {
                    capacity += maxStack; // 空格子可容纳
                }
            }
            return capacity >= count;
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

        /// <summary>
        /// 原子制作：先在内存副本上预演（扣材料 + 加成品），全部成功后才开事务写库并提交，
        /// 最后替换内存——保证内存与数据库一致，任一步失败时内存与数据库都不变。
        /// </summary>
        public bool TryCraft(RecipeInfo recipe, out string failReason)
        {
            failReason = null;
            if (recipe == null)
            {
                failReason = "配方为空";
                return false;
            }

            // 1. 内存预演：在副本上扣料 + 加成品，失败时副本直接丢弃，原状态不动
            var snapshot = CloneSlots();
            if (!ApplyRemoveTo(snapshot, recipe.Materials))
            {
                failReason = "材料不足";
                return false;
            }
            if (!ApplyAddTo(snapshot, recipe.ResultItemId, recipe.ResultCount, out failReason))
            {
                return false;
            }

            // 2. 原子提交：事务写库，成功才原地更新内存并通知
            try
            {
                SqliteManager.Instance.RunInTransaction(() =>
                {
                    foreach (var slot in snapshot)
                    {
                        _bagDao.UpdateSlot(slot);
                    }
                });
            }
            catch (System.Exception e)
            {
                failReason = "数据库提交失败：" + e.Message;
                return false;
            }

            // 3. 提交成功后才更新内存（此时数据库与内存已一致）
            ApplySnapshotToLive(snapshot);
            EventBus.Emit(EventName.Bag_Changed);
            return true;
        }

        /// <summary>深拷贝当前背包，供预演使用</summary>
        private List<BagSlotInfo> CloneSlots()
        {
            var clone = new List<BagSlotInfo>(_slotList.Count);
            foreach (var s in _slotList)
            {
                clone.Add(new BagSlotInfo { SlotId = s.SlotId, ItemId = s.ItemId, ItemCount = s.ItemCount });
            }
            return clone;
        }

        /// <summary>把预演副本的内容原地写回当前背包（不替换列表引用，外部持有的引用依然有效）</summary>
        private void ApplySnapshotToLive(List<BagSlotInfo> snapshot)
        {
            for (int i = 0; i < _slotList.Count && i < snapshot.Count; i++)
            {
                _slotList[i].SlotId = snapshot[i].SlotId;
                _slotList[i].ItemId = snapshot[i].ItemId;
                _slotList[i].ItemCount = snapshot[i].ItemCount;
            }
        }

        /// <summary>在指定副本上扣除材料（纯内存，不写库、不通知）</summary>
        private bool ApplyRemoveTo(List<BagSlotInfo> slots, Dictionary<int, int> materials)
        {
            foreach (var kv in materials)
            {
                int itemId = kv.Key;
                int need = kv.Value;
                foreach (var slot in slots)
                {
                    if (need <= 0) break;
                    if (slot.ItemId == itemId && slot.ItemCount > 0)
                    {
                        int remove = Mathf.Min(slot.ItemCount, need);
                        slot.ItemCount -= remove;
                        need -= remove;
                        if (slot.ItemCount <= 0)
                        {
                            slot.ItemId = 0;
                            slot.ItemCount = 0;
                        }
                    }
                }
                if (need > 0) return false; // 该材料不足
            }
            return true;
        }

        /// <summary>在指定副本上加入成品（纯内存，不写库、不通知）</summary>
        private bool ApplyAddTo(List<BagSlotInfo> slots, int itemId, int count, out string reason)
        {
            reason = null;
            if (count <= 0)
            {
                reason = "成品数量非法";
                return false;
            }

            ItemInfo item = _itemDao.GetItemById(itemId);
            if (item == null)
            {
                reason = "成品物品不存在";
                return false;
            }

            int maxStack = item.MaxStack;
            int remain = count;

            // 1. 优先堆叠已有同类
            foreach (var slot in slots)
            {
                if (remain <= 0) break;
                if (slot.ItemId == itemId && slot.ItemCount < maxStack)
                {
                    int add = Mathf.Min(maxStack - slot.ItemCount, remain);
                    slot.ItemCount += add;
                    remain -= add;
                }
            }

            // 2. 剩余放空格子
            if (remain > 0)
            {
                foreach (var slot in slots)
                {
                    if (remain <= 0) break;
                    if (slot.ItemId == 0)
                    {
                        int add = Mathf.Min(maxStack, remain);
                        slot.ItemId = itemId;
                        slot.ItemCount = add;
                        remain -= add;
                    }
                }
            }

            if (remain > 0)
            {
                reason = "背包空间不足";
                return false;
            }
            return true;
        }
    }
}