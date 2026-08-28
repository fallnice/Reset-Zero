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
        // 1. 初始化核心底层
        SqliteManager.Instance.Init();

        if (UIManager.Instance == null)
        {
            gameObject.AddComponent<UIManager>();
        }

        // 2. 初始化业务控制器
        BagController = new BagController();
        BagController.Init();

        CraftController = new CraftController();
        CraftController.Init(BagController);

        UIController = new UIController();
        UIController.Init();

        // 3. 初始化UI面板，绑定Controller
        BagView bagView = FindObjectOfType<BagView>(true);
        CraftView craftView = FindObjectOfType<CraftView>(true);

        // 交给UIManager
        UIManager.Instance.RegisterPanel(bagView);
        UIManager.Instance.RegisterPanel(craftView);
        // 绑定Controller
        bagView.SetController(BagController);
        craftView.SetController(CraftController);
        

    }

    private void Update()
    {
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