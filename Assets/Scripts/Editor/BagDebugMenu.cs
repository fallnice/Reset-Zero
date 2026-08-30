#if UNITY_EDITOR
using Controller;
using Core;
using Model;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 背包调试菜单项（Tools/背包调试/...），替代原运行时调试脚本 DebugTool
    /// 迁移原因：DebugTool 依赖旧 Input Manager 的 F1~F5 触发，在 Active Input Handling
    /// = Input System Only 下会抛 InvalidOperationException；且调试入口本就不该进入包体。
    /// 本文件位于 Editor 文件夹下，只在编辑器编译、不参与打包，也不依赖任何输入系统。
    /// </summary>
    public static class BagDebugMenu
    {
        private const string MENU_ROOT = "Tools/背包调试/";

        // 物品 ID 与 items 表保持一致（1001 生铁 / 1002 木头 / 1003 胶带 / 1004、1005 名称待补）
        private const int ID_IRON = 1001;
        private const int ID_WOOD = 1002;
        private const int ID_TAPE = 1003;
        private const int ADD_COUNT = 5;
        private const int ADD_ALL_COUNT = 10;

        private static readonly int[] AllMaterialIds = { 1001, 1002, 1003, 1004, 1005 };

        // ===== 单项添加（priority 差值小于 11 归为同一组）=====

        [MenuItem(MENU_ROOT + "添加 生铁 x5", false, 11)]
        private static void AddIron()
        {
            AddItem(ID_IRON, ADD_COUNT);
        }

        [MenuItem(MENU_ROOT + "添加 生铁 x5", true)]
        private static bool AddIronValidate()
        {
            return IsPlayMode();
        }

        [MenuItem(MENU_ROOT + "添加 木头 x5", false, 12)]
        private static void AddWood()
        {
            AddItem(ID_WOOD, ADD_COUNT);
        }

        [MenuItem(MENU_ROOT + "添加 木头 x5", true)]
        private static bool AddWoodValidate()
        {
            return IsPlayMode();
        }

        [MenuItem(MENU_ROOT + "添加 胶带 x5", false, 13)]
        private static void AddTape()
        {
            AddItem(ID_TAPE, ADD_COUNT);
        }

        [MenuItem(MENU_ROOT + "添加 胶带 x5", true)]
        private static bool AddTapeValidate()
        {
            return IsPlayMode();
        }

        // ===== 批量操作 =====

        [MenuItem(MENU_ROOT + "每种材料 x10", false, 31)]
        private static void AddAllMaterials()
        {
            IInventory inventory = GetInventory();
            if (inventory == null) return;

            int successCount = 0;
            foreach (int itemId in AllMaterialIds)
            {
                if (inventory.AddItem(itemId, ADD_ALL_COUNT)) successCount++;
            }

            // Editor-only 代码使用 Debug.Log 是安全的：不参与运行时，不存在每帧 GC 与 IO 开销
            Debug.Log($"[BagDebugMenu] 批量添加完成：成功 {successCount}/{AllMaterialIds.Length} 种");
        }

        [MenuItem(MENU_ROOT + "每种材料 x10", true)]
        private static bool AddAllMaterialsValidate()
        {
            return IsPlayMode();
        }

        [MenuItem(MENU_ROOT + "清空背包", false, 51)]
        private static void ClearBag()
        {
            BagController bag = GetBagController();
            if (bag == null) return;

            List<BagSlotInfo> slots = bag.GetAllSlots();
            if (slots == null)
            {
                Debug.LogWarning("[BagDebugMenu] 背包数据未初始化，请先进入运行模式并等待 GameRoot 初始化完成");
                return;
            }

            // RemoveItem 只改写格子内部的 ItemId/ItemCount，不增删列表元素，
            // 因此遍历过程中集合结构保持不变，这里用 foreach 是安全的
            foreach (BagSlotInfo slot in slots)
            {
                if (slot.ItemId != 0)
                    bag.RemoveItem(slot.ItemId, slot.ItemCount);
            }

            Debug.Log("[BagDebugMenu] 背包已清空");
        }

        [MenuItem(MENU_ROOT + "清空背包", true)]
        private static bool ClearBagValidate()
        {
            return IsPlayMode();
        }

        // ===== 内部实现 =====

        /// <summary>
        /// 菜单是否可用：背包是运行时数据（GameRoot 在 Awake 中初始化），
        /// 编辑模式下没有实例，用 validate 直接置灰比点击后报错体验更好
        /// </summary>
        private static bool IsPlayMode()
        {
            return EditorApplication.isPlaying;
        }

        /// <summary>
        /// 添加一个物品并输出结果到控制台
        /// </summary>
        private static void AddItem(int itemId, int count)
        {
            IInventory inventory = GetInventory();
            if (inventory == null) return;

            bool ok = inventory.AddItem(itemId, count);
            Debug.Log(ok
                ? $"[BagDebugMenu] 添加物品 ID:{itemId} ×{count} 成功"
                : $"[BagDebugMenu] 添加物品 ID:{itemId} ×{count} 失败（物品不存在或背包已满）");
        }

        /// <summary>
        /// 获取背包接口（BagController 实现了 IInventory）
        /// </summary>
        private static IInventory GetInventory()
        {
            return GetBagController();
        }

        /// <summary>
        /// 获取场景中的背包控制器：GameRoot 是运行时单例，编辑模式与初始化失败时都拿不到
        /// </summary>
        private static BagController GetBagController()
        {
            // 菜单项已通过 validate 置灰，这里仍判空，防止播放状态切换过程中的时序问题
            if (GameRoot.Instance == null)
            {
                Debug.LogWarning("[BagDebugMenu] 未找到 GameRoot 实例，该菜单只能在运行模式下使用");
                return null;
            }

            BagController bag = GameRoot.Instance.BagController;
            if (bag == null)
            {
                Debug.LogWarning("[BagDebugMenu] 背包控制器未初始化，请检查场景中是否存在 SqliteManager");
                return null;
            }

            return bag;
        }
    }
}
#endif
