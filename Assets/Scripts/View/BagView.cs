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
        private bool _slotsInited;      // 格子是否已生成，防止重复
        private bool _refreshWarned;    // 同类 Warning 只打印一次

        /// <summary>
        /// 绑定控制器，初始化30个格子
        /// </summary>
        public void SetController(BagController controller)
        {
            if (controller == null)
            {
                Debug.LogError("[BagView] SetController 传入的 controller 为 null");
                return;
            }

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

        // 生成30个空格子（只执行一次，防止重复生成）
        private void InitSlots()
        {
            if (_slotsInited) return;
            _slotsInited = true;

            if (slotPrefab == null)
            {
                Debug.LogError("[BagView] slotPrefab 未赋值，无法生成格子");
                return;
            }
            if (gridParent == null)
            {
                Debug.LogError("[BagView] gridParent 未赋值，无法生成格子");
                return;
            }

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
            if (_controller == null)
            {
                if (!_refreshWarned)
                {
                    _refreshWarned = true;
                    Debug.LogWarning("[BagView] RefreshUI 时 controller 为 null，请先调用 SetController");
                }
                return;
            }

            List<BagSlotInfo> slotDataList = _controller.GetAllSlots();
            if (slotDataList == null) return;

            // 取较小值，防止数据格子数超过已生成的 UI 格子数导致越界
            int count = Mathf.Min(slotDataList.Count, _slotItemList.Count);
            for (int i = 0; i < count; i++)
            {
                BagSlotInfo data = slotDataList[i];

                // itemId 为 0 表示空格子，跳过数据库查询
                ItemInfo itemInfo = data.ItemId == 0 ? null : _itemDao.GetItemById(data.ItemId);
                string itemName = itemInfo?.Name ?? "";
                Sprite icon = LoadItemIcon(data.ItemId);
                _slotItemList[i].SetData(data.ItemId, data.ItemCount, icon, itemName);
            }

            // 容量文本在循环外只更新一次
            if (capacityText != null)
            {
                int used = _controller.GetUsedSlotCount();
                int max = _controller.MaxSlot;
                capacityText.text = $"背包 ({used}/{max})";
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