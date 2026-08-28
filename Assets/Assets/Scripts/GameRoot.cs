using Core;
using Controller;
using UnityEngine;
using View;
using Role.Core;
using Role.Input;

public class GameRoot : MonoBehaviour
{
    public static GameRoot Instance { get; private set; }

    public BagController BagController { get; private set; }
    public IInventory Inventory => BagController;  // 对外暴露接口，联机时可替换实现
    public CraftController CraftController { get; private set; }
    public UIController UIController { get; private set; }

    // UI 输入提供者——角色可能在 GameRoot 之后才初始化，延迟获取后缓存
    private IUiInputProvider _uiInput;
    private bool _uiInputMissingWarned;   // 同类 Warning 只打印一次
    private bool _initialized;             // 模块初始化是否成功（失败则 Update 不再处理输入）

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitAllModule();
    }

    /// <summary>
    /// 按顺序初始化所有模块：底层→数据→业务→UI
    /// </summary>
    private void InitAllModule()
    {
        // 1. 初始化核心底层（SqliteManager 依赖场景中的持久化对象，必须判空）
        if (SqliteManager.Instance == null)
        {
            Debug.LogError("[GameRoot] 场景中缺少 SqliteManager，数据库无法初始化，请将其挂到场景中的持久化物体上");
            return;
        }
        if (!SqliteManager.Instance.Init())
        {
            Debug.LogError("[GameRoot] 数据库初始化失败，中止启动");
            return;
        }

        if (UIManager.Instance == null)
        {
            gameObject.AddComponent<UIManager>();
        }

        // 2. 初始化业务控制器
        BagController = new BagController();
        BagController.Init();

        CraftController = new CraftController();
        CraftController.Init(BagController);

        // 3. 查找并注册 UI 面板（场景中可能缺失，逐个判空降级）
        BagView bagView = FindObjectOfType<BagView>(true);
        CraftView craftView = FindObjectOfType<CraftView>(true);

        if (bagView == null)
        {
            Debug.LogWarning("[GameRoot] 场景中缺少 BagView，背包面板不可用");
        }
        else
        {
            UIManager.Instance.RegisterPanel(bagView);
            bagView.SetController(BagController);
        }

        if (craftView == null)
        {
            Debug.LogWarning("[GameRoot] 场景中缺少 CraftView，制作面板不可用");
        }
        else
        {
            UIManager.Instance.RegisterPanel(craftView);
            craftView.SetController(CraftController);
        }

        // 4. 面板注册完成后再初始化 UI 控制器（Init 里会设置面板初始显示状态）
        UIController = new UIController();
        UIController.Init();

        _initialized = true;
    }

    private void Update()
    {
        // 初始化失败（如缺 SqliteManager）时不再处理输入，避免空引用
        if (!_initialized) return;

        // 延迟获取 UI 输入提供者（角色可能在 GameRoot 之后才初始化，首次非 null 后缓存）
        if (_uiInput == null)
        {
            _uiInput = FindObjectOfType<PlayerInputProvider>();
            if (_uiInput == null)
            {
                if (!_uiInputMissingWarned)
                {
                    _uiInputMissingWarned = true;
                    Debug.LogWarning("[GameRoot] 未找到 PlayerInputProvider，UI 快捷键暂不可用");
                }
                return;
            }
        }

        // I 键：开关背包面板
        if (_uiInput.OpenBagPressed)
        {
            var bag = UIManager.Instance.GetPanel<BagView>();
            if (bag != null && bag.gameObject.activeSelf)
                UIController.CloseBagPanel();
            else
                UIController.OpenBagPanel();
        }

        // C 键：开关制作面板
        if (_uiInput.OpenCraftPressed)
        {
            var craft = UIManager.Instance.GetPanel<CraftView>();
            if (craft != null && craft.gameObject.activeSelf)
                UIController.CloseCraftPanel();
            else
                UIController.OpenCraftPanel();
        }

        // Tab 键：背包/制作互斥切换
        if (_uiInput.TogglePanelPressed)
        {
            UIController.ToggleBagAndCraft();
        }

        // ESC 键：关闭所有面板
        if (_uiInput.CloseAllPressed)
        {
            UIController.CloseAllPanels();
        }
    }

    private void OnApplicationQuit()
    {
        Instance = null;
    }
}