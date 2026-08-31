---
description: Unity 项目核心架构约束与命名规范（MVC + EventBus + 分层状态机）
alwaysApply: true
enabled: true
---

# Unity 项目架构与命名规范

## 项目背景
- Unity 2022.3 LTS（2022.3.57f1），背包 + 制造 → 开放世界角色游戏
- 定位：毕业设计 / 求职 Demo，按商业级代码标准要求
- 目标架构：MVC + EventBus + 分层状态机

## 架构约束（必须遵守）
- 每个 UI 功能模块采用 MVC：`XxxController`（逻辑）+ `XxxView`（显示）。Controller 不直接操作 UI，View 只负责渲染和转发事件
- 模块间通信统一走 `EventBus`，禁止 Controller 之间直接互相引用调用
- 角色控制采用分层状态机：`FullBodyStateMachine`（下半身 Idle/Walk/Run/Jump/Fall）与 `UpperBodyStateMachine`（上半身瞄准/攻击/交互）叠加运行，互不干扰
- 共享数据放 `Blackboard`，状态协调走 `Coordinator`，事件通知走 `EventBus`
- 配置数据统一用 `ScriptableObject`（如 `CharacterConfig`），不在代码里写魔法数字

## 接口抽象（为联机/多人预留）
- 输入统一通过 `IInputProvider` 接口获取，禁止在业务脚本里直接 `Input.GetKey`
- 背包通过 `IInventory` 接口抽象，为未来联网预留
- 交互对象实现 `IInteractable` 接口，新增交互类型时扩展该接口而非写 if-else

## 命名规范
- 类名 PascalCase，方法 PascalCase，字段 camelCase，私有字段用 `_` 前缀（如 `_inputProvider`）
- 常量用全大写下划线（如 `MAX_SLOTS`）
- 事件名集中在 `EventName` 静态类中定义，命名格式 `模块_动作`（如 `Interaction_TargetChanged`）
- 布尔字段用 `is/has/can` 前缀

## 目录规范
- 脚本根目录 `D:/Learn/Assets/Scripts/`（对应 Unity 工程里的 `Assets/Scripts/`）
- 现有子目录：Controller、Core、DAO、Interaction、Model、Role、View（更细的约定见「无 Unity 环境开发约束」规则）
- 每个模块独立目录，脚本文件与类名一一对应
