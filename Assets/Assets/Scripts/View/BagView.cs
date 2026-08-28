using Controller;
using Core;
using Dao;
using Model;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 背包面板UI（MVC-View层）
    /// </summary>
    public class BagView : MonoBehaviour
    {
        [Header("预制体与父节点")]
        [SerializeField] private BagSlotItem slotPrefab; // 格子预制体
        [SerializeField] private Transform gridParent;   // 格子父节点（Grid Layout Group）
        [Header("背包容量")]
        [SerializeField] private Text capacityText;

        private BagController _controller;
        private readonly List<BagSlotItem> _slotItemList = new List<BagSlotItem>();
        private ItemDao _itemDao = new ItemDao(); // 物品配置数据访问
        private EventBus.SubscriptionToken _bagChangedToken;

        /// <summary>
        /// 绑定控制器，初始化30个格子
        /// </summary>
        public void SetController(BagController controller)
        {
            _controller = controller;
            InitSlots();
            RefreshUI();

            // 订阅：背包变化 → 自动刷新 UI
            _bagChangedToken = EventBus.Subscribe(EventName.Bag_Changed, _ => RefreshUI());
        }

        private void OnDestroy()
        {
            _bagChangedToken?.Dispose();
        }

        // 生成30个空格子
        private void InitSlots()
        {
            for (int i = 0; i < 30; i++)
            {
                BagSlotItem slot = Instantiate(slotPrefab, gridParent);
                _slotItemList.Add(slot);
            }
        }

        /// <summary>
        /// 刷新整个背包UI（数据变动时由Controller调用）
        /// </summary>
        public void RefreshUI()
        {
            List<BagSlotInfo> slotDataList = _controller.GetAllSlots();

            for (int i = 0; i < slotDataList.Count; i++)
            {
                BagSlotInfo data = slotDataList[i];

                ItemInfo itemInfo = _itemDao.GetItemById(data.ItemId);
                string itemName = itemInfo?.Name ?? "";
                Sprite icon = LoadItemIcon(data.ItemId);
                _slotItemList[i].SetData( data.ItemId, data.ItemCount, icon,itemName);

                if (capacityText != null)
                {
                    int used = _controller.GetUsedSlotCount();
                    int max = _controller.MaxSlot;
                    capacityText.text = $"背包 ({used}/{max})";
                }
            }
        }

        // 动态加载物品图标（图标放在 Resources/ItemIcons 下，以物品ID命名）
        private Sprite LoadItemIcon(int itemId)
        {
            if (itemId == 0) return null;
            Sprite icon = Resources.Load<Sprite>($"ItemIcons/{itemId}");
            return icon;
        }
    }
}