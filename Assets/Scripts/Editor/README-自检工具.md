# AI 自检工具（P0）使用说明

> 本目录是「AI 盲写代码自检闭环」的落地框架。本项目可能由多个 AI 模型接力开发
> （作者在家/公司用不同会话、不同模型），**本说明 + 代码注释是接力者唯一的上下文，
> 动手前请先完整读完本文件与相关代码的注释。**

## 一、这套工具解决什么

Unity 工程里「**编译通过 ≠ 能跑**」。场景/配置类错误（管理器不在场景、组件没挂、
`[SerializeField]` 引用没赋值）藏在 `.unity`/`.prefab` 文件里，纯看代码发现不了，
只能打开编辑器人肉检查——而 AI 盲写代码时通常不具备这个条件。

本工具让 Unity **无头模式**自动完成两类检查，结果写文件，AI 读文件即可定位问题：

| 检查 | 覆盖问题 | 实现 |
| --- | --- | --- |
| 编译检查 | 代码编译错误（`error CS`） | 无头编译 + 读 Unity 日志 |
| 场景自检 | 管理器缺失、组件缺失、序列化引用未赋值 | `SceneAudit.cs` 加载场景逐项检查 |

## 二、文件一览

| 文件 | 作用 |
| --- | --- |
| `Assets/Scripts/Editor/SceneAudit.cs` | 场景自检框架：检查器注册机制 + 内置 6 项检查 + 菜单/命令行入口 |
| `Assets/Scripts/Editor/CompileCheck.cs` | 编译检查辅助：Unity.exe 路径探测 + 命令生成 + 菜单复制命令 |
| `Tools/run_check.ps1` | 一键自检：先编译检查、再场景自检，汇总 PASS/FAIL |

## 三、怎么用

> ⚠️ **重要（血的教训）**：无头自检与「打开着本项目的 Unity 编辑器」**互斥**。
> 同一项目被编辑器和无头实例同时打开是 Unity 官方不支持的用法（项目锁 +
> 并发写 `Library` 缓存），可能崩溃或损坏缓存；曾因此发生过编辑器进程被
> 误杀的事故。`run_check.ps1` 已内置检测：检测到编辑器打开会**中止**（退出码 3），
> 请先保存并关闭编辑器再跑。`-Force` 可跳过该检查，但不推荐。
> 手动跑命令行方式 B 时同理：跑之前先确认编辑器没开着本项目。

### 方式 A：一键脚本（推荐）

```powershell
# 在项目根目录执行（Tools/run_check.ps1 会自动向上找工程根）
powershell -ExecutionPolicy Bypass -File Tools\run_check.ps1
```

找不到 Unity.exe 时（公司机器路径可能不同），二选一：

```powershell
# 方式 1：直接传参
powershell -ExecutionPolicy Bypass -File Tools\run_check.ps1 -UnityPath "D:\xxx\Unity.exe"

# 方式 2：设置环境变量（推荐，一劳永逸）
$env:UNITY_PATH = "D:\xxx\Unity.exe"
powershell -ExecutionPolicy Bypass -File Tools\run_check.ps1
```

### 方式 B：手动命令

Unity 编辑器菜单 `Tools/自检/复制编译检查命令`（或 `复制场景自检命令`）可直接复制
拼好的命令；也可以手写：

```powershell
# 编译检查
"<Unity.exe>" -batchmode -nographics -quit -projectPath "<工程>" -logFile "%TEMP%\unity_compile_check.log"

# 场景自检
"<Unity.exe>" -batchmode -nographics -quit -projectPath "<工程>" -executeMethod EditorTools.SceneAudit.RunFromCommandLine -logFile "%TEMP%\unity_scene_audit_run.log"
```

## 四、结果文件与判读

| 文件 | 内容 |
| --- | --- |
| `%TEMP%\unity_compile_check.log` | Unity 完整日志。`error CS` 行 = 编译错误；末尾 `Exiting batchmode successfully now!` = 无致命错误 |
| `%TEMP%\unity_scene_audit.log` | 逐场景逐检查项 `[PASS]`/`[FAIL]`，末尾 `SCENE_AUDIT_RESULT: PASS/FAIL` |

**读日志的正确姿势**：编译错误看 `error CS`；场景自检看 `[FAIL]` 行，它直接说了缺什么。

可用环境变量覆盖（可选）：
- `SCENE_AUDIT_SCENE`：只自检指定场景（完整路径，如 `D:\unity\Project\Assets\Scenes\SampleScene.unity`）
- `SCENE_AUDIT_LOG`：自检结果日志路径（默认 `%TEMP%\unity_scene_audit.log`）

## 五、如何扩展（新增检查项）

检查器是注册机制，加一项 = 写一个返回 `CheckResult` 的方法 + 一行注册。

```csharp
// 1. 写检查方法
private static SceneAudit.CheckResult CheckMyThing()
{
    bool ok = UnityEngine.Object.FindObjectOfType<MyThing>(true) != null;
    return new SceneAudit.CheckResult(ok, ok ? "存在" : "缺失");
}

// 2. 在 SceneAudit.RegisterDefaultCheckers() 里注册一行
RegisterChecker("MyThing 存在", CheckMyThing);
```

已有内置检查项（与当前工程配置对齐）：
`GameRoot` / `SqliteManager` / `PlayerInputProvider` / `BagView` 引用 / `CraftView` 引用 / `CameraFollow` 引用。

## 六、本项目约定（重要，接力者必读）

- 正式游戏代码（`Assets/Scripts` 非 Editor 目录）**禁止 `Debug.Log`**（每帧 GC/IO 开销），
  只用 `Debug.LogError` / `Debug.LogWarning` 且要判空降级。
- Editor-only 代码（`Assets/Scripts/Editor`）可以用 `Debug.Log`（不参与运行时）。
- 中文注释、中文 `[MenuItem]` 名是项目惯例。
- `PlayerInputActions.inputactions` 是**手写 JSON**，改它要保持 JSON 合法；
  对应 `PlayerInputActions.cs` 内嵌了同款 JSON（`m_AssetJson`），Unity 导入
  `.inputactions` 时会**自动同步**该字段（已实测：只改 `.inputactions` 的 processors，
  导入后 `.cs` 内嵌 JSON 同步更新、代码体不受影响）。若在编辑器里改 inputactions
  且勾选 Generate C# Class，则会**整体重新生成**该 `.cs`，注意别覆盖手写部分。
- 不要动 `GameRoot` 的模块初始化顺序；场景缺东西时游戏用判空降级而非崩溃。
- 数据库用 `SqliteManager`（场景中持久化对象，`[DefaultExecutionOrder(-100)]`），
  事务统一走 `SqliteManager.BeginTransaction/Commit/Rollback`。

## 七、待完善清单（公司模型接力点）

当前框架可跑，以下按优先级排列，属「到公司完善」的范围：

- [ ] 公司机器上验证 `run_check.ps1` 能跑通（重点：Unity 路径探测，必要时设 `UNITY_PATH`）
- [ ] 加「角色预制体（Player.prefab）组件完整性」检查项（当前只查场景内引用）
- [ ] 加「`Resources/ItemIcons` 图标是否齐全」检查项（按 `items` 表 ID 对照文件）
- [ ] 把自检接入日常流程：改完代码 → 跑 `run_check.ps1` → 修 `[FAIL]` → 再跑
- [ ] 视情况把自检封装进 `BagDebugMenu` 同级的常用工具（如 git 提交前自动跑）

## 八、历史背景

- 2026-08-30 落地本框架（见项目根 `开发日志.md` 对应日期小节）。
- 提出背景：评估「接入 Unity MCP 做自动化」的替代方案，结论是写 Editor 静态类
  自检能拿到约 80% 核心收益且零污染。
- 本次落地前的进展：无头编译已手动跑通 3 次（`-batchmode -nographics -quit`），
  但未沉淀成工具；场景自检此前完全未实现。
