using Core;
using Controller;
using DAO;
using Dao;
using Interaction;
using Model;
using UnityEngine;
using UnityEngine.SceneManagement;
using View;
using Role.Core;
using Role.Input;
using Role;

public class GameRoot : MonoBehaviour
{
    public static GameRoot Instance { get; private set; }

    public BagController BagController { get; private set; }
    public IInventory Inventory => BagController;  // 对外暴露接口，联机时可替换实现
    public CraftController CraftController { get; private set; }
    public BonusController BonusController { get; private set; }
    public QuestController QuestController { get; private set; }
    public GameplayFeedbackController GameplayFeedbackController { get; private set; }
    public UIController UIController { get; private set; }

    [Header("任务系统")]
    [SerializeField] private QuestDefinition questDefinition;

    // UI 输入提供者——角色可能在 GameRoot 之后才初始化，延迟获取后缓存
    private IUiInputProvider _uiInput;
    private bool _uiInputMissingWarned;   // 同类 Warning 只打印一次

    // 玩家角色——用于把玩家 CombatStats 注入 BonusController；延迟获取后缓存
    private CharacterRoot _playerCharacter;
    private bool _playerCharacterMissingWarned;

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

        BonusController = new BonusController();
        BonusController.Init(BagController);

        GameplayFeedbackController = new GameplayFeedbackController(new ItemDao());
        GameplayFeedbackController.Initialize();

        QuestController = new QuestController(BagController, new QuestDao());
        if (questDefinition == null)
        {
            Debug.LogWarning("[GameRoot] 未配置 QuestDefinition，任务系统不可用");
        }
        else if (QuestController.Initialize(questDefinition))
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            InitializeQuestGivers();
        }

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

        // 延迟注入玩家 CombatStats 到 BonusController
        ResolvePlayerCombatStats();

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

    /// <summary>
    /// 延迟查找玩家角色并把其 CombatStats 注入 BonusController；
    /// 角色可能在 GameRoot 之后才初始化，首次非 null 后缓存不再查找。
    /// </summary>
    private void ResolvePlayerCombatStats()
    {
        if (BonusController == null || _playerCharacter != null) return;

        CharacterRoot[] roots = FindObjectsOfType<CharacterRoot>();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].IsPlayerControlled)
            {
                _playerCharacter = roots[i];
                break;
            }
        }

        if (_playerCharacter == null)
        {
            if (!_playerCharacterMissingWarned)
            {
                _playerCharacterMissingWarned = true;
                Debug.LogWarning("[GameRoot] 未找到玩家 CharacterRoot，加成道具暂不可用");
            }
            return;
        }

        BonusController.SetCombatStats(_playerCharacter.Context.CombatStats);
    }

    /// <summary> 新场景加载后为其中的任务发布者注入持久化任务控制器。 </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InitializeQuestGivers();
    }

    /// <summary> 查找所有已加载场景中的任务发布者并注入控制器。 </summary>
    private void InitializeQuestGivers()
    {
        if (QuestController == null) return;
        QuestGiverInteractable[] questGivers = FindObjectsOfType<QuestGiverInteractable>(true);
        if (questGivers.Length == 0)
        {
            Debug.LogWarning("[GameRoot] 场景中缺少 QuestGiverInteractable，任务无法接取或交付");
            return;
        }
        for (int i = 0; i < questGivers.Length; i++)
            questGivers[i].Initialize(QuestController);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GameplayFeedbackController?.Dispose();
        QuestController?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GameplayFeedbackController?.Dispose();
        QuestController?.Dispose();
        Instance = null;
    }

    private void OnValidate()
    {
        if (questDefinition == null)
            Debug.LogWarning("[GameRoot] QuestDefinition 未配置", this);
    }
}