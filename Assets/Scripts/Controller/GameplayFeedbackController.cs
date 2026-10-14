using System;
using System.Collections.Generic;
using Core;
using Dao;
using Model;

namespace Controller
{
    /// <summary>
    /// 将核心玩法领域事件转换为玩家可见提示，避免业务控制器和视图直接耦合反馈文案。
    /// </summary>
    public sealed class GameplayFeedbackController : IDisposable
    {
        private readonly Dictionary<int, ItemInfo> _items;
        private EventBus.SubscriptionToken _pickupToken;
        private EventBus.SubscriptionToken _craftSuccessToken;
        private EventBus.SubscriptionToken _craftFailToken;
        private EventBus.SubscriptionToken _lootToken;
        private EventBus.SubscriptionToken _questCompletedToken;
        private bool _isSubscribed;

        /// <summary> 创建玩法反馈控制器并缓存物品配置，避免每次反馈重复查询数据库。 </summary>
        public GameplayFeedbackController(ItemDao itemDao)
        {
            if (itemDao == null) throw new ArgumentNullException(nameof(itemDao));
            _items = itemDao.GetAllItems() ?? new Dictionary<int, ItemInfo>();
        }

        /// <summary> 订阅拾取、制造、掉落与任务结果事件。 </summary>
        public void Initialize()
        {
            if (_isSubscribed) return;

            _pickupToken = EventBus.Subscribe(EventName.Pickup_Completed, HandlePickupCompleted);
            _craftSuccessToken = EventBus.Subscribe(EventName.Craft_Success, HandleCraftSuccess);
            _craftFailToken = EventBus.Subscribe(EventName.Craft_Fail, HandleCraftFail);
            _lootToken = EventBus.Subscribe(EventName.Loot_Dropped, HandleLootDropped);
            _questCompletedToken = EventBus.Subscribe(EventName.Quest_Completed, HandleQuestCompleted);
            _isSubscribed = true;
        }

        /// <summary> 释放全部领域事件订阅。 </summary>
        public void Dispose()
        {
            if (!_isSubscribed) return;

            _pickupToken?.Dispose();
            _craftSuccessToken?.Dispose();
            _craftFailToken?.Dispose();
            _lootToken?.Dispose();
            _questCompletedToken?.Dispose();
            _pickupToken = null;
            _craftSuccessToken = null;
            _craftFailToken = null;
            _lootToken = null;
            _questCompletedToken = null;
            _isSubscribed = false;
        }

        /// <summary> 显示成功拾取的物品和数量。 </summary>
        private void HandlePickupCompleted(object[] args)
        {
            if (!TryReadItemAndCount(args, 1, 2, out int itemId, out int count)) return;
            PublishToast("获得 " + GetItemName(itemId) + " ×" + count);
        }

        /// <summary> 显示成功制造的成品和数量。 </summary>
        private void HandleCraftSuccess(object[] args)
        {
            if (!TryReadItemAndCount(args, 1, 2, out int itemId, out int count)) return;
            PublishToast("制作完成：" + GetItemName(itemId) + " ×" + count);
        }

        /// <summary> 显示制造失败的具体原因。 </summary>
        private void HandleCraftFail(object[] args)
        {
            if (args == null || args.Length < 2 || !(args[1] is string reason)
                || string.IsNullOrWhiteSpace(reason))
                return;

            PublishToast("制作失败：" + reason);
        }

        /// <summary> 显示敌人死亡后的世界掉落结果。 </summary>
        private void HandleLootDropped(object[] args)
        {
            if (args == null || args.Length < 3 || !(args[2] is int spawnedCount) || spawnedCount <= 0)
                return;

            PublishToast("敌人掉落了 " + spawnedCount + " 件物品");
        }

        /// <summary> 显示任务完成和奖励入包结果。 </summary>
        private void HandleQuestCompleted(object[] args)
        {
            if (!TryReadItemAndCount(args, 1, 2, out int itemId, out int count))
            {
                PublishToast("任务已完成");
                return;
            }

            PublishToast("任务完成，获得 " + GetItemName(itemId) + " ×" + count);
        }

        /// <summary> 从事件参数中读取合法物品 ID 和数量。 </summary>
        private static bool TryReadItemAndCount(
            object[] args,
            int itemIndex,
            int countIndex,
            out int itemId,
            out int count)
        {
            itemId = 0;
            count = 0;
            if (args == null || itemIndex < 0 || countIndex < 0
                || itemIndex >= args.Length || countIndex >= args.Length)
                return false;
            if (!(args[itemIndex] is int parsedItemId) || !(args[countIndex] is int parsedCount)
                || parsedItemId <= 0 || parsedCount <= 0)
                return false;

            itemId = parsedItemId;
            count = parsedCount;
            return true;
        }

        /// <summary> 返回物品显示名；配置缺失时使用稳定的 ID 回退文本。 </summary>
        private string GetItemName(int itemId)
        {
            return _items.TryGetValue(itemId, out ItemInfo item)
                && item != null
                && !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : "物品 " + itemId;
        }

        /// <summary> 通过统一 UI 事件请求显示反馈。 </summary>
        private static void PublishToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            EventBus.Emit(EventName.UI_Toast, message);
        }
    }
}
