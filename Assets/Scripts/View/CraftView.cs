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

        private CraftController _controller;
        private ItemDao _itemDao = new ItemDao();
        private bool _materialsReady;   // 材料列表相关引用是否就绪
        private bool _inventoryWarned;  // 同类 Warning 只打印一次

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
            if (controller == null)
            {
                Debug.LogError("[CraftView] SetController 传入的 controller 为 null");
                return;
            }

            _controller = controller;

            // 一次性校验材料列表相关引用，避免后续每次刷新反复判空
            if (materialItemPrefab == null || materialContainer == null)
            {
                Debug.LogError("[CraftView] materialItemPrefab 或 materialContainer 未赋值，材料列表不可用");
                _materialsReady = false;
            }
            else
            {
                _materialsReady = true;
            }

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
            if (minusBtn == null || plusBtn == null || craftBtn == null)
            {
                Debug.LogError("[CraftView] 按钮引用未完整赋值（minusBtn/plusBtn/craftBtn），无法注册点击事件");
                return;
            }
            minusBtn.onClick.AddListener(OnMinusCount);
            plusBtn.onClick.AddListener(OnPlusCount);
            craftBtn.onClick.AddListener(OnCraftButtonClick);
        }

        // 初始化左侧配方列表
        private void InitRecipeList()
        {
            if (recipeItemPrefab == null || recipeListParent == null)
            {
                Debug.LogError("[CraftView] recipeItemPrefab 或 recipeListParent 未赋值，无法生成配方列表");
                return;
            }

            Dictionary<int, RecipeInfo> allRecipes = _controller.GetAllRecipes();
            if (allRecipes == null || allRecipes.Count == 0)
            {
                Debug.LogWarning("[CraftView] 配方数据为空");
                return;
            }

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
            foreach (var key in allRecipes.Keys)
            {
                OnSelectRecipe(key);
                break;
            }
        }

        // 选中配方，刷新右侧全部详情
        private void OnSelectRecipe(int recipeId)
        {
            _currentRecipeId = recipeId;
            _craftCount = 1;
            if (craftCountText != null)
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
            if (_controller == null) return;

            Dictionary<int, RecipeInfo> allRecipes = _controller.GetAllRecipes();
            if (allRecipes == null || !allRecipes.TryGetValue(_currentRecipeId, out RecipeInfo recipe))
            {
                Debug.LogWarning($"[CraftView] 配方不存在 ID:{_currentRecipeId}");
                return;
            }

            ItemInfo itemInfo = _itemDao.GetItemById(recipe.ResultItemId);

            // 1. 成品基础信息
            if (resultIconImg != null)
                resultIconImg.sprite = LoadItemIcon(recipe.ResultItemId);
            if (itemNameText != null)
                itemNameText.text = itemInfo?.Name ?? "未知物品";
            if (descText != null)
                descText.text = itemInfo?.Desc ?? "";

            // GameRoot/Inventory 可能尚未初始化，判空降级
            IInventory inventory = GameRoot.Instance?.Inventory;
            if (inventory != null && ownCountText != null)
            {
                int ownResultCount = inventory.GetItemTotalCount(recipe.ResultItemId);
                ownCountText.text = $"拥有：{ownResultCount}";
            }

            // 2. 刷新材料列表（垂直生成）
            RefreshMaterialList(recipe);
        }

        // 刷新材料列表：销毁旧的，生成新的
        private void RefreshMaterialList(RecipeInfo recipe)
        {
            if (!_materialsReady) return;

            IInventory inventory = GameRoot.Instance?.Inventory;
            if (inventory == null)
            {
                if (!_inventoryWarned)
                {
                    _inventoryWarned = true;
                    Debug.LogWarning("[CraftView] Inventory 未就绪，跳过材料数量显示");
                }
                return;
            }

            // 清空旧材料条目
            foreach (var item in _materialItemList)
            {
                if (item != null) Destroy(item.gameObject);
            }
            _materialItemList.Clear();

            // 逐个生成材料行，垂直排列在容器里
            foreach (var mat in recipe.Materials)
            {
                CraftMaterialItem item = Instantiate(materialItemPrefab, materialContainer);
                ItemInfo matInfo = _itemDao.GetItemById(mat.Key);
                Sprite icon = LoadItemIcon(mat.Key);

                int haveCount = inventory.GetItemTotalCount(mat.Key);
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
                if (craftCountText != null)
                    craftCountText.text = $"制作 × {_craftCount}";
                RefreshDetail(); // 同步刷新材料需求
            }
        }

        // 增加制作数量
        private void OnPlusCount()
        {
            _craftCount++;
            if (craftCountText != null)
                craftCountText.text = $"制作 × {_craftCount}";
            RefreshDetail(); // 同步刷新材料需求
        }

        // 点击制作按钮，转发给Controller处理业务
        private void OnCraftButtonClick()
        {
            if (_controller == null)
            {
                Debug.LogWarning("[CraftView] 制作按钮点击时 controller 为 null");
                return;
            }

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
                EventBus.Emit(EventName.UI_Toast, "材料不足");
            }
        }

        // 加载物品图标
        private Sprite LoadItemIcon(int itemId)
        {
            if (itemId == 0) return null;
            return Resources.Load<Sprite>($"ItemIcons/{itemId}");
        }
    }
}