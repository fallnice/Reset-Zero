---
description: Unity 性能红线——零 GC 与缓存原则
alwaysApply: true
enabled: true
---

# Unity 性能规范（零 GC 红线）

## 每帧热点禁止分配内存（必须）
- Update/FixedUpdate 中禁止 `new` 对象、LINQ、字符串拼接、装箱
- 物理查询用 `OverlapSphereNonAlloc` / `RaycastNonAlloc` 并预分配数组复用（如 `Collider[16]`）
- 避免在循环里调用 `GetComponent` 或 `Find`

## 缓存引用（必须）
- `GetComponent`、`EventBus` 获取结果必须缓存到字段，不每帧重复获取
- 跨 MonoBehaviour 引用用 `Start` 或 `Update` 延迟缓存，不在 `Awake` 里缓存（Awake 顺序不保证）

## 降频与过滤
- 不需要每帧精度的检测（交互检测、目标搜索）用定时器降频（如 0.1s 一次）
- 物理检测必须用 `LayerMask` 过滤，跳过无关 Collider

## 事件广播
- 只有状态真正变化时才 Emit 事件，不变化不广播
- 事件订阅在 OnEnable/OnDisable 成对管理，避免内存泄漏

## 调试输出
- 正式代码禁止 `Debug.Log`（GC + 控制台 IO 是真实性能隐患）
- 需要调试用条件编译或完成后删除
