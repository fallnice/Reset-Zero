---
description: 无 Unity 编辑器环境下的离线开发约束（本项目核心工作模式）
alwaysApply: true
enabled: true
---

# 无 Unity 环境开发约束

## 环境现状（写代码前必须知道）
- 本机无 Unity Editor：代码无法编译、运行，也无法在 Inspector 里查看或赋值
- 项目根目录即 `D:/Learn/`，Unity 的 Assets 内容位于 `D:/Learn/Assets/Assets/`（多一层 Assets，是「抠 Assets」时的产物）
- 缺 `Packages/` 与 `ProjectSettings/`：依赖包清单与项目级配置未随 Assets 一起保留
- `.meta` 文件完整（163 个），资源 GUID 引用可保留，不要新增或删除 .meta

## 代码必须静态正确（没有编译器兜底）
- 严格使用 Unity 2022.3 LTS 的 API，禁止臆造类名 / 方法 / 枚举 / 命名空间
- 不确定的 API 一律标注「待验证」，不得装作确定
- 引用其他脚本的类 / 方法前，先确认该脚本真实存在（项目现有 47 个 .cs）
- 新增代码不依赖本机未安装的第三方 Package；若必须用，在手操清单里标注包名

## 每次产出必须附「Unity 手动操作清单」
无法在 Inspector 里拖拽赋值，生成脚本时必须同步说明需手动完成的操作：
- `[SerializeField]` / public 引用字段：拖什么、拖到哪个物体
- 脚本要挂到哪个 GameObject（给出物体名与层级路径）
- 需要手动创建的 Layer / Tag / Input Action
- 需要设置的 Animator 参数、物理、碰撞等

## 环境配置缺口（将来重建工程时要补）
- 输入使用 Input System（PlayerInputActions），Input Action 需在 Unity 里重建
- SQLite 插件位于 Plugins 目录，重建工程后确认导入
- Layer（可交互层等）、Tag 属于 ProjectSettings，需手动重建

## 场景与 Prefab 不可见
- 无法查看场景层级与 Prefab 挂载关系，涉及场景结构的改动必须用文字描述清楚
- 依赖场景中已有物体的脚本，要写明物体名称与层级路径

## 目录约定
- 脚本根目录：`D:/Learn/Assets/Assets/Scripts/`（对应 Unity 工程里的 Assets/Scripts/）
- 现有子目录：Controller、Core、DAO、Interaction、Model、Role、View
- 新脚本放入对应模块目录，不散落在根目录
