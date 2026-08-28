---
description: Unity 生命周期陷阱与踩坑经验总结
alwaysApply: true
enabled: true
---

# Unity 生命周期陷阱（踩坑经验）

## Awake 执行顺序不保证（重要）
- Unity 不保证不同对象的 `Awake` 调用顺序，跨 MonoBehaviour 的引用不要在 `Awake` 中缓存
- 正确做法：`Awake` 只缓存自身引用，`Start` 或 `Update` 中延迟获取外部引用（首次非 null 后缓存）

## OnEnable / OnDisable 配对
- 在 `OnEnable` 里订阅 EventBus，就必须在 `OnDisable` 里取消订阅
- 禁止在 `OnEnable` 中对自己所在物体 `SetActive(false)`（触发 OnDisable → 取消订阅 → 永远收不到事件，形成「自杀」）

## Trigger 回调的前提
- `OnTriggerEnter` 等回调要求碰撞双方至少一方有 `Rigidbody`
- 角色用 `CharacterController`（无 Rigidbody）时，子物体 Collider 无法触发 Trigger 回调
- 方案：用 `Physics.OverlapSphereNonAlloc` 主动检测代替依赖物理回调

## UI 层级陷阱
- 不要在挂脚本的物体上叠加全屏 Image（会挡屏）
- 简单 UI 提示直接把脚本挂在 Text 上，用 `text.text` 空串 = 隐藏、有值 = 显示，避免 Panel/Image/CanvasGroup 的复杂度

## 调试原则
- 同类 Warning 只打印一次（用 bool 标志），避免刷屏
- 修复 Bug 后写清楚根因和修复方式，沉淀到开发日志
