#if UNITY_EDITOR
using Combat;
using Core;
using Enemy;
using Enemy.Navigation;
using Role;
using Role.Input;
using Role.Interaction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using View;

namespace EditorTools
{
    /// <summary>
    /// 场景自检工具（P0：AI 自检闭环 · 第 2 项）
    ///
    /// 背景：Unity 工程里「编译通过 ≠ 能跑」。场景/配置类错误（管理器不在场景、
    /// 组件没挂、SerializeField 引用没赋值）藏在 .unity/.prefab 文件里，纯看代码
    /// 发现不了，只能开编辑器人肉检查。本工具让 Unity 无头模式自动完成这些检查，
    /// 结果写文件，AI 读文件形成「盲写代码 → 自检 → 修复 → 再自检」闭环。
    ///
    /// 用法：
    ///   1. 编辑器菜单：Tools/自检/场景自检（当前打开的场景会被覆盖，先保存）
    ///   2. 命令行（推荐，配合 Tools/run_check.ps1）：
    ///      Unity.exe -batchmode -nographics -quit -projectPath <工程> \
    ///               -executeMethod EditorTools.SceneAudit.RunFromCommandLine \
    ///               -logFile <unity自身日志>
    ///
    /// 结果文件：默认 %TEMP%/unity_scene_audit.log
    ///   - 逐场景逐检查项输出 [PASS]/[FAIL]
    ///   - 末尾 SCENE_AUDIT_RESULT: PASS / FAIL（脚本/外部以此判断）
    ///   - 可用环境变量覆盖：SCENE_AUDIT_LOG（日志路径）、SCENE_AUDIT_SCENE（只查指定场景）
    ///
    /// 扩展：新增检查项 = 调用 RegisterChecker(name, fn)（见 RegisterDefaultCheckers 示例）。
    /// </summary>
    public static class SceneAudit
    {
        public const string ResultMarkerPass = "SCENE_AUDIT_RESULT: PASS";
        public const string ResultMarkerFail = "SCENE_AUDIT_RESULT: FAIL";

        private const string DefaultLogFileName = "unity_scene_audit.log";
        private const string SceneEnvVar = "SCENE_AUDIT_SCENE";  // 可选：只自检指定场景
        private const string LogEnvVar = "SCENE_AUDIT_LOG";      // 可选：覆盖结果日志路径

        /// <summary> 结果日志路径（静态构造时解析，支持环境变量覆盖）</summary>
        public static string ResultLogPath { get; private set; }

        // ── 检查器定义 ────────────────────────────────────────────

        public sealed class CheckResult
        {
            public bool Pass;
            public string Message;
            public CheckResult(bool pass, string message) { Pass = pass; Message = message; }
        }

        public delegate CheckResult SceneChecker();

        private sealed class CheckDef
        {
            public string Name;
            public SceneChecker Run;
        }

        private static readonly List<CheckDef> Checkers = new List<CheckDef>();

        static SceneAudit()
        {
            string envLog = Environment.GetEnvironmentVariable(LogEnvVar);
            ResultLogPath = string.IsNullOrEmpty(envLog)
                ? Path.Combine(Path.GetTempPath(), DefaultLogFileName)
                : envLog;

            RegisterDefaultCheckers();
        }

        /// <summary>
        /// 注册自定义检查器（扩展点：新增检查项 = 调用本方法并注册一个返回 CheckResult 的委托）
        /// </summary>
        public static void RegisterChecker(string name, SceneChecker checker)
        {
            if (string.IsNullOrEmpty(name) || checker == null) return;
            Checkers.Add(new CheckDef { Name = name, Run = checker });
        }

        // ── 入口 ──────────────────────────────────────────────────

        [MenuItem("Tools/自检/场景自检", false, 1)]
        public static void RunFromMenu()
        {
            // 菜单模式会切换当前场景，先让用户保存未保存的修改（批处理模式此调用恒返回 true）
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool pass = RunAllChecksAndReport();
            Debug.Log(pass
                ? $"[SceneAudit] 自检全部通过，结果文件：{ResultLogPath}"
                : $"[SceneAudit] 存在未通过项，详见：{ResultLogPath}");
        }

        /// <summary>
        /// 命令行入口（-executeMethod）。跑完按结果退出：0=通过，1=存在失败项。
        /// 注意：本方法仅用于批处理，不要在菜单里调用（会直接关闭编辑器）。
        /// </summary>
        public static void RunFromCommandLine()
        {
            bool pass = RunAllChecksAndReport();
            EditorApplication.Exit(pass ? 0 : 1);
        }

        // ── 主流程 ────────────────────────────────────────────────

        private static bool RunAllChecksAndReport()
        {
            var sb = new StringBuilder();
            int totalPass = 0;
            int totalFail = 0;
            List<string> targetScenes = GetTargetScenes();
            if (targetScenes.Count == 0)
            {
                totalFail++;
                sb.AppendLine("[FAIL] 目标场景: 未找到可自检场景（检查 SCENE_AUDIT_SCENE、Build Settings 或 Assets/Scenes）");
            }

            foreach (string scenePath in targetScenes)
            {
                sb.AppendLine($"=== Scene: {scenePath} ===");

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                foreach (CheckDef def in Checkers)
                {
                    CheckResult result;
                    try
                    {
                        result = def.Run();
                    }
                    catch (Exception e)
                    {
                        // 检查器自身异常也记为 FAIL，防止单点异常中断整个自检
                        result = new CheckResult(false, "检查器异常: " + e.Message);
                    }

                    if (result.Pass)
                    {
                        totalPass++;
                        sb.AppendLine($"[PASS] {def.Name}: {result.Message}");
                    }
                    else
                    {
                        totalFail++;
                        sb.AppendLine($"[FAIL] {def.Name}: {result.Message}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"TOTAL: pass={totalPass} fail={totalFail}");
            sb.AppendLine(totalFail == 0 ? ResultMarkerPass : ResultMarkerFail);

            File.WriteAllText(ResultLogPath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[SceneAudit] 自检完成 pass={totalPass} fail={totalFail}，结果文件：{ResultLogPath}");
            return totalFail == 0;
        }

        /// <summary>
        /// 目标场景选择：SCENE_AUDIT_SCENE 环境变量 > Build Settings 启用的场景 > Assets/Scenes 兜底
        /// </summary>
        private static List<string> GetTargetScenes()
        {
            var list = new List<string>();

            string envScene = Environment.GetEnvironmentVariable(SceneEnvVar);
            if (!string.IsNullOrEmpty(envScene))
            {
                if (File.Exists(envScene)) list.Add(envScene);
                else Debug.LogError($"[SceneAudit] SCENE_AUDIT_SCENE 指定的场景不存在：{envScene}");
                return list;
            }

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            if (buildScenes != null && buildScenes.Length > 0)
            {
                foreach (EditorBuildSettingsScene s in buildScenes)
                {
                    if (s.enabled && File.Exists(s.path)) list.Add(s.path);
                }
            }
            if (list.Count > 0) return list;

            const string sceneRoot = "Assets/Scenes";
            if (Directory.Exists(sceneRoot))
            {
                foreach (string f in Directory.GetFiles(sceneRoot, "*.unity", SearchOption.AllDirectories))
                    list.Add(f.Replace('\\', '/'));
            }
            return list;
        }

        // ── 默认检查器（与当前工程实际配置对齐）──────────────────────
        //
        // 下面每一项对应一个「编译能过但运行必炸/功能缺失」的坑。
        // 新增检查：写一个返回 CheckResult 的方法，然后在 RegisterDefaultCheckers 里
        // RegisterChecker("名字", 方法) 一行注册。

        private static void RegisterDefaultCheckers()
        {
            RegisterChecker("GameRoot 存在", CheckGameRoot);
            RegisterChecker("SqliteManager 存在", CheckSqliteManager);
            RegisterChecker("PlayerInputProvider 存在", CheckPlayerInputProvider);
            RegisterChecker("InteractionDetector 存在", CheckInteractionDetector);
            RegisterChecker("BagView 引用完整", CheckBagViewRefs);
            RegisterChecker("CraftView 引用完整", CheckCraftViewRefs);
            RegisterChecker("ToastView 存在", CheckToastView);
            RegisterChecker("CameraFollow 引用完整", CheckCameraFollow);
            RegisterChecker("敌人 AI 组件完整", CheckEnemyAI);
            RegisterChecker("A* 导航网格配置", CheckEnemyNavigationGrid);
        }

        /// <summary> 启动入口：GameRoot 缺失 = 无任何模块初始化 </summary>
        private static CheckResult CheckGameRoot()
        {
            bool found = UnityEngine.Object.FindObjectOfType<GameRoot>(true) != null;
            return new CheckResult(found, found ? "场景中存在 GameRoot" : "场景中缺少 GameRoot（启动入口缺失，整局不可玩）");
        }

        /// <summary> 数据库：SqliteManager 缺失时 GameRoot 直接 LogError 中止启动 </summary>
        private static CheckResult CheckSqliteManager()
        {
            // 编辑模式下 Awake 未执行，静态单例 Instance 为 null，必须用场景搜索
            bool found = UnityEngine.Object.FindObjectOfType<SqliteManager>(true) != null;
            return new CheckResult(found, found ? "场景中存在 SqliteManager" : "场景中缺少 SqliteManager（数据库无法初始化，GameRoot 中止启动）");
        }

        /// <summary> 输入桥：PlayerInputProvider 缺失 = UI 快捷键与相机旋转全部不可用 </summary>
        private static CheckResult CheckPlayerInputProvider()
        {
            bool found = UnityEngine.Object.FindObjectOfType<PlayerInputProvider>(true) != null;
            return new CheckResult(found, found ? "场景中存在 PlayerInputProvider" : "场景中缺少 PlayerInputProvider（UI 快捷键与相机旋转不可用）");
        }

        /// <summary> 交互检测器：缺失时靠近可交互物无提示、按 E 无反应，且不报错（静默失效） </summary>
        private static CheckResult CheckInteractionDetector()
        {
            bool found = UnityEngine.Object.FindObjectOfType<InteractionDetector>(true) != null;
            return new CheckResult(found, found
                ? "场景中存在 InteractionDetector"
                : "场景中缺少 InteractionDetector（靠近可交互物无提示、按 E 无反应，且不报错，属于静默失效）");
        }

        /// <summary> 背包面板：slotPrefab/gridParent 缺失 = 格子无法生成（LogError） </summary>
        private static CheckResult CheckBagViewRefs()
        {
            BagView bag = UnityEngine.Object.FindObjectOfType<BagView>(true);
            if (bag == null) return new CheckResult(false, "场景中缺少 BagView（背包面板不可用）");

            // capacityText 缺失只影响容量文本显示（代码有判空降级），一并检查方便发现
            string[] required = { "slotPrefab", "gridParent", "capacityText" };
            return CheckSerializedRefs(bag, required, null);
        }

        /// <summary> 制作面板：必填引用缺失 = 对应功能 LogError / 按钮点击无效 </summary>
        private static CheckResult CheckCraftViewRefs()
        {
            CraftView craft = UnityEngine.Object.FindObjectOfType<CraftView>(true);
            if (craft == null) return new CheckResult(false, "场景中缺少 CraftView（制作面板不可用）");

            // 必填：缺失会 LogError 或按钮失效
            string[] required = { "recipeItemPrefab", "recipeListParent", "materialItemPrefab", "materialContainer", "minusBtn", "plusBtn", "craftBtn" };
            // 次要：代码有判空降级，缺失不崩，只影响显示
            string[] optional = { "resultIconImg", "itemNameText", "ownCountText", "descText", "craftCountText" };
            return CheckSerializedRefs(craft, required, optional);
        }

        /// <summary> 全局浮动提示：缺失时业务仍运行，但玩家看不到失败原因 </summary>
        private static CheckResult CheckToastView()
        {
            bool found = UnityEngine.Object.FindObjectOfType<ToastView>(true) != null;
            return new CheckResult(found, found ? "场景中存在 ToastView" : "场景中缺少 ToastView（全局浮动提示不可见）");
        }

        /// <summary> 相机跟随：target 未赋值 = 相机不跟随（代码直接 return） </summary>
        private static CheckResult CheckCameraFollow()
        {
            CameraFollow cam = UnityEngine.Object.FindObjectOfType<CameraFollow>(true);
            if (cam == null) return new CheckResult(false, "场景中缺少 CameraFollow（应挂在主相机上）");

            var so = new SerializedObject(cam);
            if (GetRef(so, "target") == null)
                return new CheckResult(false, "CameraFollow.target 未赋值（相机不跟随角色）");
            return new CheckResult(true, "CameraFollow 存在且 target 已赋值");
        }

        /// <summary> 敌人 AI：Brain 的角色/输入/配置缺一都会静默失效 </summary>
        private static CheckResult CheckEnemyAI()
        {
            EnemyBrain[] brains = UnityEngine.Object.FindObjectsOfType<EnemyBrain>(true);
            if (brains.Length == 0)
                return new CheckResult(true, "场景未启用敌人 AI，跳过组件检查");

            var issues = new List<string>();
            for (int i = 0; i < brains.Length; i++)
            {
                EnemyBrain brain = brains[i];
                if (brain.GetComponent<CharacterRoot>() == null)
                    issues.Add($"{brain.name}: 缺少 CharacterRoot");
                if (brain.GetComponent<AIInputProvider>() == null)
                    issues.Add($"{brain.name}: 缺少 AIInputProvider");
                if (brain.GetComponent<CharacterController>() == null)
                    issues.Add($"{brain.name}: 缺少 CharacterController");
                if (brain.GetComponent<Role.Controllers.EquipmentController>() == null)
                    issues.Add($"{brain.name}: 缺少 EquipmentController");

                CharacterRoot root = brain.GetComponent<CharacterRoot>();
                if (root != null)
                {
                    var rootSo = new SerializedObject(root);
                    SerializedProperty playerControlled = rootSo.FindProperty("isPlayerControlled");
                    SerializedProperty faction = rootSo.FindProperty("faction");
                    if (playerControlled == null || playerControlled.boolValue)
                        issues.Add($"{brain.name}: CharacterRoot.IsPlayerControlled 必须关闭");
                    if (faction == null || faction.enumValueIndex != (int)Faction.Enemy)
                        issues.Add($"{brain.name}: CharacterRoot.Faction 必须为 Enemy");
                }

                var so = new SerializedObject(brain);
                EnemyConfig config = GetRef(so, "config") as EnemyConfig;
                if (config == null)
                {
                    issues.Add($"{brain.name}: EnemyConfig 未赋值");
                }
                else
                {
                    if (config.navigationStoppingDistance > config.attackRange)
                        issues.Add($"{brain.name}: navigationStoppingDistance 必须 <= attackRange");
                    if (config.meleeWeapon == null)
                        issues.Add($"{brain.name}: EnemyConfig.meleeWeapon 未赋值");
                    else if (config.meleeWeapon.type != WeaponType.Melee)
                        issues.Add($"{brain.name}: EnemyConfig.meleeWeapon 必须是 Melee 类型");
                }

                UnityEngine.Object source = GetRef(so, "navigationSource");
                if (source != null && !(source is IEnemyNavigation))
                    issues.Add($"{brain.name}: Navigation Source 未实现 IEnemyNavigation");
            }

            return issues.Count == 0
                ? new CheckResult(true, $"{brains.Length} 个 EnemyBrain 组件与配置完整")
                : new CheckResult(false, string.Join("；", issues));
        }

        /// <summary> A*：存在 GridAStarNavigation 时必须且只能有一个有效场景网格 </summary>
        private static CheckResult CheckEnemyNavigationGrid()
        {
            GridAStarNavigation[] navigations = UnityEngine.Object.FindObjectsOfType<GridAStarNavigation>(true);
            if (navigations.Length == 0)
                return new CheckResult(true, "场景未启用 GridAStarNavigation，当前使用 DirectNavigation 回退");

            EnemyNavigationGrid[] grids = UnityEngine.Object.FindObjectsOfType<EnemyNavigationGrid>(true);
            if (grids.Length != 1)
                return new CheckResult(false, $"启用 A* 时场景应有且仅有 1 个 EnemyNavigationGrid，当前={grids.Length}");
            if (!grids[0].enabled || !grids[0].gameObject.activeInHierarchy)
                return new CheckResult(false, "EnemyNavigationGrid 未启用");
            if (grids[0].transform.rotation != Quaternion.identity || grids[0].transform.lossyScale != Vector3.one)
                return new CheckResult(false, "EnemyNavigationGrid 必须保持 Rotation=0、Scale=1");

            EnemyBrain[] brains = UnityEngine.Object.FindObjectsOfType<EnemyBrain>(true);
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i].GetComponent<GridAStarNavigation>() == null)
                    return new CheckResult(false, $"{brains[i].name}: 缺少 GridAStarNavigation，仍会回退 DirectNavigation");
            }

            var so = new SerializedObject(grids[0]);
            SerializedProperty ground = so.FindProperty("walkableGroundMask");
            SerializedProperty obstacle = so.FindProperty("navigationObstacleMask");
            int groundMask = ground != null ? ground.intValue : 0;
            int obstacleMask = obstacle != null ? obstacle.intValue : 0;
            if (groundMask == 0)
                return new CheckResult(false, "EnemyNavigationGrid.walkableGroundMask 为空");
            if (obstacleMask == 0)
                return new CheckResult(false, "EnemyNavigationGrid.navigationObstacleMask 为空，墙体不会参与寻路");
            if ((groundMask & obstacleMask) != 0)
                return new CheckResult(false, "EnemyNavigationGrid 的 Ground/Obstacle Mask 重叠");

            return new CheckResult(true,
                $"A* 配置有效：1 个网格，{navigations.Length} 个 GridAStarNavigation");
        }

        /// <summary>
        /// 通用：用 SerializedObject 检查 MonoBehaviour 的 [SerializeField] 私有字段引用是否已赋值。
        /// required 全空才算 PASS；optional 缺失不判 FAIL，只在 Message 里提示。
        /// </summary>
        private static CheckResult CheckSerializedRefs(MonoBehaviour component, string[] required, string[] optional)
        {
            var so = new SerializedObject(component);

            var missingRequired = new List<string>();
            if (required != null)
            {
                foreach (string field in required)
                    if (GetRef(so, field) == null) missingRequired.Add(field);
            }
            if (missingRequired.Count > 0)
                return new CheckResult(false, $"{component.GetType().Name} 必填引用未赋值: {string.Join(", ", missingRequired)}");

            var missingOptional = new List<string>();
            if (optional != null)
            {
                foreach (string field in optional)
                    if (GetRef(so, field) == null) missingOptional.Add(field);
            }
            return missingOptional.Count == 0
                ? new CheckResult(true, $"{component.GetType().Name} 引用完整")
                : new CheckResult(true, $"{component.GetType().Name} 引用完整（次要引用缺失: {string.Join(", ", missingOptional)}）");
        }

        /// <summary> 读取序列化字段引用，字段不存在返回 null </summary>
        private static UnityEngine.Object GetRef(SerializedObject so, string field)
        {
            SerializedProperty prop = so.FindProperty(field);
            return prop == null ? null : prop.objectReferenceValue;
        }
    }
}
#endif
