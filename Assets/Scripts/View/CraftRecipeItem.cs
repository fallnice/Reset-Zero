using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 左侧单个配方条目
    /// </summary>
    public class CraftRecipeItem : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Text nameText;
        [SerializeField] private Button btn;
        [SerializeField] private Image selected;

        private int _recipeId;
        private System.Action<int> _onSelectCallback;
        private bool _warned;   // 同类 Warning 只打印一次
        public int RecipeId { get; private set; }

        /// <summary>
        /// 初始化条目数据
        /// </summary>
        public void Init(int recipeId, string itemName, Sprite iconSprite, System.Action<int> onSelect)
        {
            RecipeId = recipeId;
            _recipeId = recipeId;
            if (icon != null) icon.sprite = iconSprite;
            if (nameText != null) nameText.text = itemName;
            _onSelectCallback = onSelect;

            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(OnClick);
            }
            else if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[CraftRecipeItem] btn 未赋值，配方无法点击");
            }
        }

        private void OnClick()
        {
            _onSelectCallback?.Invoke(_recipeId);
        }

        /// <summary>
        /// 设置选中框显隐
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            if (selected != null)
                selected.gameObject.SetActive(isSelected);
        }
    }
}

