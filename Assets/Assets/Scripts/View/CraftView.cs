using Controller;
using Core;
using Dao;
using Model;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 制作面板UI（MVC-View层）
    /// </summary>
    public class CraftView : MonoBehaviour
    {
        [Header("左侧配方列表")]
        [SerializeField] private CraftRecipeItem recipeItemPrefab;
        [SerializeField] private Transform recipeListParent;

        [Header("右侧详情 - 成品基础信息")]
        [SerializeField] private Image resultIconImg;
        [SerializeField] private Text itemNameText;
        [SerializeField] private Text ownCountText;
        [SerializeField] private Text descText;

        [Header("右侧详情 - 材料列表")]
        [SerializeField] private CraftMaterialItem materialItemPrefab;
        [SerializeField] private Transform materialContainer; // 材料垂直容器

        [Header("制作数量控制")]
        [SerializeField] private Button minusBtn;
        [SerializeField] private Button plusBtn;
        [SerializeField] private Text craftCountText;
        [SerializeField] private Button craftBtn;

        [Header("浮动提示")]
        [SerializeField] private Text tipText;
        [SerializeField] private float tipDuration = 2f;   // 2秒消失
        [SerializeField] private float tipFloatDistance = 60f;  // 上飘距离

        private CraftController _controller;
        private ItemDao _itemDao = new ItemDao();

        private int _currentRecipeId;
        private int _craftCount = 1;
        private readonly List<CraftRecipeItem> _recipeItemList = new List<CraftRecipeItem>();
        private readonly List<CraftMaterialItem> _materialItemList = new List<CraftMaterialItem>();
        private EventBus.SubscriptionToken _bagChangedToken;

        /// <summary>
        /// 绑定控制器，初始化列表
        /// </summary>
        public void SetController(CraftController controller)
        {
            _controller = controller;
            InitRecipeList();
            RegisterButtonEvents();

            // 订阅：背包变化 → 制作面板打开时自动刷新材料数量
            _bagChangedToken = EventBus.Subscribe(EventName.Bag_Changed, _ =>
            {
                if (gameObject.activeSelf && _currentRecipeId != 0)
                    RefreshDetail();
            });
        }

        private void OnDestroy()
        {
            _bagChangedToken?.Dispose();
        }
        private void OnEnable()
        {
            // 已初始化且有选中配方时刷新
            if (_controller != null && _currentRecipeId != 0)
            {
                RefreshDetail();
            }
        }

        // 注册按钮事件
        private void RegisterButtonEvents()
        {
            minusBtn.onClick.AddListener(OnMinusCount);
            plusBtn.onClick.AddListener(OnPlusCount);
            craftBtn.onClick.AddListener(OnCraftButtonClick);
        }

        // 初始化左侧配方列表
        private void InitRecipeList()
        {
            Dictionary<int, RecipeInfo> allRecipes = _controller.GetAllRecipes();

            foreach (var recipe in allRecipes.Values)
            {
                CraftRecipeItem item = Instantiate(recipeItemPrefab, recipeListParent);
                ItemInfo itemInfo = _itemDao.GetItemById(recipe.ResultItemId);
                string name = itemInfo?.Name ?? "未知物品";
                Sprite icon = LoadItemIcon(recipe.ResultItemId);
                item.Init(recipe.RecipeId, name, icon, OnSelectRecipe);
                _recipeItemList.Add(item);
            }

            // 默认选中第一个配方
            if (allRecipes.Count > 0)
            {
                using var enumerator = allRecipes.Keys.GetEnumerator();
                enumerator.MoveNext();
                OnSelectRecipe(enumerator.Current);
            }
        }

        // 选中配方，刷新右侧全部详情
        private void OnSelectRecipe(int recipeId)
        {
            _currentRecipeId = recipeId;
            _craftCount = 1;
            craftCountText.text = $"制作 × {_craftCount}";

            // 更新选中高亮
            foreach (var recipeItem in _recipeItemList)
            {
                recipeItem.SetSelected(recipeItem.RecipeId == recipeId);
            }

            RefreshDetail();
        }

        // 刷新右侧全部详情
        private void RefreshDetail()
        {
            RecipeInfo recipe = _controller.GetAllRecipes()[_currentRecipeId];
            ItemInfo itemInfo = _itemDao.GetItemById(recipe.ResultItemId);

            // 1. 成品基础信息
            resultIconImg.sprite = LoadItemIcon(recipe.ResultItemId);
            itemNameText.text = itemInfo?.Name ?? "未知物品";
            descText.text = itemInfo?.Desc ?? "";

            int ownResultCount = GameRoot.Instance.Inventory.GetItemTotalCount(recipe.ResultItemId);
            ownCountText.text = $"拥有：{ownResultCount}";

            // 2. 刷新材料列表（垂直生成）
            RefreshMaterialList(recipe);
        }

        // 刷新材料列表：销毁旧的，生成新的
        private void RefreshMaterialList(RecipeInfo recipe)
        {
            // 清空旧材料条目
            foreach (var item in _materialItemList)
            {
                Destroy(item.gameObject);
            }
            _materialItemList.Clear();

            // 逐个生成材料行，垂直排列在容器里
            foreach (var mat in recipe.Materials)
            {
                CraftMaterialItem item = Instantiate(materialItemPrefab, materialContainer);
                ItemInfo matInfo = _itemDao.GetItemById(mat.Key);
                Sprite icon = LoadItemIcon(mat.Key);

                int haveCount = GameRoot.Instance.Inventory.GetItemTotalCount(mat.Key);
                int needCount = mat.Value * _craftCount; // 乘以制作数量

                item.SetData(icon, needCount, haveCount, matInfo?.Name ?? "未知材料");
                _materialItemList.Add(item);
            }
        }

        // 减少制作数量
        private void OnMinusCount()
        {
            if (_craftCount > 1)
            {
                _craftCount--;
                craftCountText.text = $"制作 × {_craftCount}";
                RefreshDetail(); // 同步刷新材料需求
            }
        }

        // 增加制作数量
        private void OnPlusCount()
        {
            _craftCount++;
            craftCountText.text = $"制作 × {_craftCount}";
            RefreshDetail(); // 同步刷新材料需求
        }

        // 点击制作按钮，转发给Controller处理业务
        private void OnCraftButtonClick()
        {
            bool success = true;
            for (int i = 0; i < _craftCount; i++)
            {
                if (!_controller.DoCraft(_currentRecipeId))
                {
                    success = false;
                    break;
                }
            }

            if (success)
            {
                RefreshDetail(); // 制作完成刷新所有数据
            }
            else
            {
                ShowTip("材料不足");
            }
        }

        // 加载物品图标
        private Sprite LoadItemIcon(int itemId)
        {
            if (itemId == 0) return null;
            return Resources.Load<Sprite>($"ItemIcons/{itemId}");
        }
        /// <summary>
        /// 显示浮动提示（目前仅用于材料不足）
        /// </summary>
        private void ShowTip(string msg)
        {
            if (tipText == null) return;
            StopAllCoroutines();            // 防止多个提示叠加
            StartCoroutine(TipRoutine(msg));
        }

        private IEnumerator TipRoutine(string msg)
        {
            tipText.text = msg;
            tipText.gameObject.SetActive(true);

            // 初始：居中、全透明
            Color c = tipText.color;
            c.a = 0f;
            tipText.color = c;

            Vector3 startPos = tipText.rectTransform.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < tipDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / tipDuration;

                // 前 0.3 秒淡入，之后淡出
                float alpha = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
                c.a = Mathf.Clamp01(alpha);
                tipText.color = c;

                // 上飘
                tipText.rectTransform.anchoredPosition = startPos + Vector3.up * (tipFloatDistance * t);

                yield return null;
            }

            tipText.gameObject.SetActive(false);
            tipText.rectTransform.anchoredPosition = startPos; // 复位
        }
    }
}