using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 单个材料条件行UI：左图标，中数量，右名称
    /// </summary>
    public class CraftMaterialItem : MonoBehaviour
    {
        [SerializeField] private Image iconImg;
        [SerializeField] private Text countText;
        [SerializeField] private Text nameText;
        private bool _warned;   // 同类 Warning 只打印一次

        /// <summary>
        /// 设置材料数据
        /// </summary>
        /// <param name="icon">图标</param>
        /// <param name="needCount">需求数量</param>
        /// <param name="haveCount">玩家拥有数量</param>
        /// <param name="matName">材料名称</param>
        public void SetData(Sprite icon, int needCount, int haveCount, string matName)
        {
            if (iconImg == null || countText == null || nameText == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[CraftMaterialItem] 组件引用未完整赋值");
                }
                return;
            }

            iconImg.sprite = icon;
            countText.text = $"{needCount} / {haveCount}";
            nameText.text = matName;

            // 拥有不足标红
            countText.color = haveCount >= needCount ? Color.white : Color.red;
        }
    }
}