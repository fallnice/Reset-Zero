---
description: C# 代码风格与防御性编程规范
alwaysApply: true
enabled: true
---

# C# 代码风格规范

## 基础风格
- 使用 4 个空格缩进，不用 Tab
- 每个类、方法用 `///` 写 XML 注释说明用途
- 单行不超过 120 字符
- 复杂逻辑必须加内联注释解释「为什么」，而不是「做了什么」

## 防御性编程（必须）
- 外部依赖（GetComponent、FindObjectOfType、EventBus 获取）使用后必须判空
- 空引用要给出明确的 `Debug.LogWarning` 说明哪个对象缺失
- 集合操作前检查索引越界
- 公共方法入口对参数做合法性校验

## 空引用检查
- 优先使用 `TryGetComponent` 而非 `GetComponent`
- 序列化引用在 `OnValidate` 或运行时做空检查
- 用 `?.` 和 `??` 简化判空，但不要滥用导致掩盖问题

## 禁用项
- 禁用每帧调用 `FindObjectOfType`
- 禁用字符串硬编码的类名/方法名查找
- 禁用 `Debug.Log` 输出调试信息（正式代码里），调试完必须清理
