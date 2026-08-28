using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 单个背包格子的UI逻辑：只负责显示数据，不处理业务
    /// </summary>
    public class BagSlotItem : MonoBehaviour
    {
        [Header("组件引用")]
        public Image iconImage,iconParent;    // 物品图标
        public Text countText; // 堆叠数量
        public Text nameText;//物品名称

        /// <summary>
        /// 给格子赋值，外部调用这个方法刷新显示
        /// </summary>
        /// <param name="itemId">物品ID，0=空格子</param>
        /// <param name="count">物品数量</param>
        /// <param name="icon">物品图标</param>
        /// <param name="itemName">物品名称</param>
        public void SetData(int itemId, int count, Sprite icon,string itemName)
        {
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
