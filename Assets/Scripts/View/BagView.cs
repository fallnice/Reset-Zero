using Controller;
using Core;
using Dao;
using Model;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 背包面板UI（MVC-View层）
    /// </summary>
    public class BagView : MonoBehaviour, IModalPanel
    {
        [Header("预制体与父节点")]
        [SerializeField] private BagSlotItem slotPrefab; // 格子预制体
        [SerializeField] private Transform gridParent;   // 格子父节点（Grid Layout Group）
        [Header("背包容量")]
        [SerializeField] private Text capacityText;
        [Header("使用按钮（选中加成道具时显示）")]
        [SerializeField] private Button useBtn;

        private BagController _controller;
        private readonly List<BagSlotItem> _slotItemList = new List<BagSlotItem>();
        private ItemDao _itemDao = new ItemDao(); // 物品配置数据访问
        private EventBus.SubscriptionToken _bagChangedToken;
        private bool _slotsInited;      // 格子是否已生成，防止重复
        private bool _refreshWarned;    // 同类 Warning 只打印一次
        private int _selectedIndex = -1; // 当前选中格子索引，-1=未选中

        /// <summary>选中事件：参数为选中格子索引（-1=取消选中）。供「使用」按钮等外部订阅</summary>
        public event Action<int> SlotSelected;
        /// <summary>当前选中格子索引（-1=未选中）</summary>
        public int SelectedIndex => _selectedIndex;
        /// <summary>当前选中格子的物品ID（未选中或越界返回0）</summary>
        public int SelectedItemId
        {
            get
            {
                if (_selectedIndex < 0) return 0;
                var slots = _controller?.GetAllSlots();
                if (slots == null || _selectedIndex >= slots.Count) return 0;
                return slots[_selectedIndex].ItemId;
            }
        }

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
            RegisterUseButton();
            RefreshUseButton();

            // 订阅：背包变化 → 自动刷新 UI
            _bagChangedToken = EventBus.Subscribe(EventName.Bag_Changed, _ => RefreshUI());
        }

        private void OnDestroy()
        {
            _bagChangedToken?.Dispose();
            if (useBtn != null) useBtn.onClick.RemoveListener(OnUseButtonClick);
            foreach (var slot in _slotItemList)
            {
                slot.SlotClicked -= HandleSlotClicked;
            }
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
                slot.SetIndex(i);
                slot.SlotClicked += HandleSlotClicked;
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

                // 数据刷新后格子变空 → 自动取消选中
                if (data.ItemId == 0 && i == _selectedIndex)
                {
                    SelectSlot(-1);
                }
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

        /// <summary>
        /// 格子点击回调：有物品则选中，空格子则清除选中
        /// </summary>
        private void HandleSlotClicked(BagSlotItem slot)
        {
            var slots = _controller?.GetAllSlots();
            if (slots == null || slot.Index < 0 || slot.Index >= slots.Count) return;

            if (slots[slot.Index].ItemId == 0)
            {
                SelectSlot(-1);
                return;
            }
            SelectSlot(slot.Index);
        }

        /// <summary>
        /// 设置选中格子（-1=取消选中）：维护高亮并广播选中事件
        /// </summary>
        public void SelectSlot(int index)
        {
            if (_selectedIndex == index) return;

            // 清除旧选中高亮
            if (_selectedIndex >= 0 && _selectedIndex < _slotItemList.Count)
            {
                _slotItemList[_selectedIndex].SetSelected(false);
            }

            _selectedIndex = index;

            // 设置新选中高亮
            if (index >= 0 && index < _slotItemList.Count)
            {
                _slotItemList[index].SetSelected(true);
            }

            SlotSelected?.Invoke(index);
            RefreshUseButton();
        }

        // 注册「使用」按钮点击事件
        private void RegisterUseButton()
        {
            if (useBtn == null)
            {
                Debug.LogWarning("[BagView] useBtn 未赋值，「使用」功能不可用");
                return;
            }
            useBtn.onClick.AddListener(OnUseButtonClick);
        }

        // 点击「使用」：把选中的加成道具用掉一个
        private void OnUseButtonClick()
        {
            int itemId = SelectedItemId;
            if (itemId == 0)
            {
                Debug.LogWarning("[BagView] 未选中物品，无法使用");
                return;
            }

            if (GameRoot.Instance?.BonusController == null)
            {
                Debug.LogWarning("[BagView] BonusController 未初始化，无法使用物品");
                return;
            }

            // 使用成功后 RemoveItem 会广播 Bag_Changed → RefreshUI → 自动刷新选中与按钮状态
            if (!GameRoot.Instance.BonusController.UseItem(itemId))
            {
                Debug.LogWarning($"[BagView] 使用物品失败 ID:{itemId}（数量不足或非加成道具？）");
            }
        }

        // 刷新「使用」按钮显隐：选中且为加成道具才显示
        private void RefreshUseButton()
        {
            if (useBtn == null) return;
            int itemId = SelectedItemId;
            bool show = itemId != 0
                && GameRoot.Instance?.BonusController != null
                && GameRoot.Instance.BonusController.IsBonusItem(itemId);
            useBtn.gameObject.SetActive(show);
        }
    }
}