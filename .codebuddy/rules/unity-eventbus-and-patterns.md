---
description: EventBus 使用规范与 MVC/状态机实现模式（按需参考）
alwaysApply: false
enabled: true
---

# EventBus 与架构模式指南

## EventBus 使用规范
- 所有事件名集中在 `EventName` 静态类定义，禁止散落硬编码字符串
- 发布者只管 `Emit`，订阅者在 `OnEnable`/`OnDisable` 成对订阅/取消
- 事件处理器要能容忍重复触发和异常（错误隔离，单个订阅者异常不影响其他订阅者）
- 优先用细粒度事件（如 `Interaction_TargetChanged`），避免大而全的「万能事件」

## MVC 模式
- Controller 持有数据和逻辑，View 持有 UI 引用和渲染
- View 通过事件把用户操作转发给 Controller，Controller 通过事件把数据变化通知 View
- View 里禁止写业务逻辑

## 分层状态机模式
- `FullBodyStateMachine` 管理移动/跳跃/落地（下半身）
- `UpperBodyStateMachine` 管理攻击/瞄准/交互（上半身），与 FullBody 叠加运行
- 状态切换通过事件触发，每个状态类内聚自己的进入/退出逻辑

## 数据持久化
- 所有需要落盘的配方、物品、存档走 `SqliteManager` + DAO 层封装
- 业务代码不直接拼 SQL，通过 DAO 方法访问
