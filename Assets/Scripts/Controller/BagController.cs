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
            return TryAddItem(itemId, count, out _);
        }

        /// <summary> 原子添加物品并返回明确失败原因。 </summary>
        public bool TryAddItem(int itemId, int count, out InventoryOperationResult result)
        {
            result = InventoryOperationResult.InvalidArgument;
            if (itemId <= 0 || count <= 0)
            {
                Debug.LogWarning($"添加物品参数非法 ID:{itemId} 数量:{count}");
                return false;
            }
            if (!IsPersistenceReady())
            {
                result = InventoryOperationResult.NotInitialized;
                Debug.LogWarning("[BagController] 数据库或背包未初始化，添加操作已取消");
                return false;
            }
            ItemInfo item = _itemDao.GetItemById(itemId);
            if (item == null || item.MaxStack <= 0)
            {
                result = InventoryOperationResult.ItemNotFound;
                Debug.LogWarning($"[BagController] 物品配置不存在或堆叠上限非法 ID:{itemId}");
                return false;
            }

            List<BagSlotInfo> snapshot = CloneSlots();
            if (!ApplyAddTo(snapshot, itemId, count, out string reason))
            {
                result = InventoryOperationResult.InsufficientSpace;
                Debug.LogWarning($"添加物品失败 ID:{itemId} 数量:{count} 原因:{reason}");
                return false;
            }

            try
            {
                SqliteManager.Instance.RunInTransaction(() =>
                {
                    foreach (BagSlotInfo slot in snapshot)
                        _bagDao.UpdateSlot(slot);
                });
            }
            catch (System.Exception exception)
            {
                result = InventoryOperationResult.PersistenceFailed;
                Debug.LogWarning($"添加物品写库失败，内存未改变。原因:{exception.Message}");
                return false;
            }

            ApplySnapshotToLive(snapshot);
            result = InventoryOperationResult.Success;
            EventBus.Emit(EventName.Bag_ItemAdded, itemId, count);
            EventBus.Emit(EventName.Bag_Changed);
            return true;
        }

        /// <summary>
        /// 原子移除物品：先在副本中预演，事务提交成功后才更新内存并发送领域事件。
        /// </summary>
        public bool RemoveItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0)
            {
                Debug.LogWarning($"移除物品参数非法 ID:{itemId} 数量:{count}");
                return false;
            }
            if (!IsPersistenceReady())
            {
                Debug.LogWarning("[BagController] 数据库或背包未初始化，移除操作已取消");
                return false;
            }

            List<BagSlotInfo> snapshot = CloneSlots();
            if (!ApplyRemoveItemTo(snapshot, itemId, count))
            {
                int total = GetItemTotalCount(itemId);
                Debug.LogWarning($"物品不足 ID:{itemId} 拥有:{total} 需要:{count}");
                return false;
            }

            try
            {
                SqliteManager.Instance.RunInTransaction(() =>
                {
                    foreach (BagSlotInfo slot in snapshot)
                    {
                        _bagDao.UpdateSlot(slot);
                    }
                });
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"移除物品写库失败 ID:{itemId} 数量:{count} 原因:{exception.Message}");
                return false;
            }

            ApplySnapshotToLive(snapshot);
            EventBus.Emit(EventName.Bag_ItemRemoved, itemId, count);
            EventBus.Emit(EventName.Bag_Changed);
            return true;
        }

        /// <summary>
        /// 预检能否放入指定数量的物品（不实际修改背包）
        /// 供调用方在批量操作前判断空间是否足够
        /// </summary>
        public bool CanAddItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0 || _itemDao == null || _slotList == null) return false;
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
            if (!IsPersistenceReady())
            {
                failReason = "背包或数据库未初始化";
                return false;
            }
            if (!ValidateRecipe(recipe, out failReason))
                return false;

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

        /// <summary> 检查背包数据访问和数据库连接是否已完成初始化。 </summary>
        private bool IsPersistenceReady()
        {
            return SqliteManager.Instance != null && SqliteManager.Instance.IsReady
                && _bagDao != null && _itemDao != null && _slotList != null;
        }

        /// <summary> 校验配方成品和全部材料，防止非法配置绕过消耗或触发空引用。 </summary>
        private bool ValidateRecipe(RecipeInfo recipe, out string failReason)
        {
            failReason = null;
            ItemInfo resultItem = _itemDao.GetItemById(recipe.ResultItemId);
            if (recipe.ResultItemId <= 0 || recipe.ResultCount <= 0
                || resultItem == null || resultItem.MaxStack <= 0)
            {
                failReason = "成品配置非法或不存在";
                return false;
            }
            if (recipe.Materials == null || recipe.Materials.Count == 0)
            {
                failReason = "配方未配置材料";
                return false;
            }

            foreach (KeyValuePair<int, int> material in recipe.Materials)
            {
                if (material.Key <= 0 || material.Value <= 0
                    || _itemDao.GetItemById(material.Key) == null)
                {
                    failReason = $"材料配置非法或不存在 ID:{material.Key} 数量:{material.Value}";
                    return false;
                }
            }

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
            if (snapshot == null || snapshot.Count != _slotList.Count)
            {
                Debug.LogWarning("[BagController] 背包副本槽位数量不一致，已拒绝更新内存");
                return;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                _slotList[i].SlotId = snapshot[i].SlotId;
                _slotList[i].ItemId = snapshot[i].ItemId;
                _slotList[i].ItemCount = snapshot[i].ItemCount;
            }
        }

        /// <summary>在背包副本中预演单种物品移除，不触碰实时内存或数据库。</summary>
        private static bool ApplyRemoveItemTo(List<BagSlotInfo> slots, int itemId, int count)
        {
            if (slots == null || itemId <= 0 || count <= 0) return false;

            int available = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                BagSlotInfo slot = slots[i];
                if (slot.ItemId == itemId && slot.ItemCount > 0)
                    available += slot.ItemCount;
            }
            if (available < count) return false;

            int remaining = count;
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                BagSlotInfo slot = slots[i];
                if (slot.ItemId != itemId || slot.ItemCount <= 0) continue;

                int removed = Mathf.Min(slot.ItemCount, remaining);
                slot.ItemCount -= removed;
                remaining -= removed;
                if (slot.ItemCount <= 0)
                {
                    slot.ItemId = 0;
                    slot.ItemCount = 0;
                }
            }

            return remaining == 0;
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