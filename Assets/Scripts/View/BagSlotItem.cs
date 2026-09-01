using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 单个背包格子的UI逻辑：负责显示数据 + 点击选中（不含业务）
    /// </summary>
    public class BagSlotItem : MonoBehaviour, IPointerClickHandler
    {
        [Header("组件引用")]
        public Image iconImage, iconParent;    // 物品图标
        public Text countText; // 堆叠数量
        public Text nameText;  // 物品名称
        [SerializeField] private Image selected; // 选中高亮框（仿 CraftRecipeItem，prefab 配独立 Image）

        private bool _warned;   // 同类 Warning 只打印一次

        /// <summary>格子索引（由 BagView 分配）</summary>
        public int Index { get; private set; } = -1;
        /// <summary>当前是否处于选中状态</summary>
        public bool IsSelected { get; private set; }

        /// <summary>点击回调（由 BagView 订阅）</summary>
        public event Action<BagSlotItem> SlotClicked;

        public void SetIndex(int index)
        {
            Index = index;
        }

        /// <summary>
        /// 设置选中框显隐（仿 CraftRecipeItem）
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            if (selected != null)
            {
                selected.gameObject.SetActive(isSelected);
                IsSelected = isSelected;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            SlotClicked?.Invoke(this);
        }

        /// <summary>
        /// 给格子赋值，外部调用这个方法刷新显示
        /// </summary>
        /// <param name="itemId">物品ID，0=空格子</param>
        /// <param name="count">物品数量</param>
        /// <param name="icon">物品图标</param>
        /// <param name="itemName">物品名称</param>
        public void SetData(int itemId, int count, Sprite icon, string itemName)
        {
            // 组件引用缺失时静默跳过，避免空引用
            if (iconImage == null || iconParent == null || countText == null || nameText == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[BagSlotItem] 组件引用未完整赋值，格子无法显示");
                }
                return;
            }

            // 空格子：隐藏图标，清空文字
            if (itemId == 0 || count <= 0)
            {
                iconImage.enabled = false;
                iconParent.enabled = false;
                countText.text = "";
                nameText.text = "";
                return;
            }

            // 有物品：显示图标和数量
            iconParent.enabled = true;
            iconImage.enabled = true;
            iconImage.sprite = icon;

            // 数量大于1才显示数字，单个物品不显示
            countText.text = count >= 1 ? count.ToString() : "";
            nameText.text = itemName;
        }
    }
}
