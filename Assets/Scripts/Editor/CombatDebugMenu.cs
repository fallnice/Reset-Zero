#if UNITY_EDITOR
using Combat;
using Core;
using Role.Controllers;
using Role.Core;   // Blackboard
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 战斗系统调试菜单（Tools/战斗调试/...）
    ///
    /// 用途：在正式入口（拾取交互、背包「使用」按钮）落地之前，
    /// 手动验证「装备武器 → 攻击加成 → 加成道具生效」这条链路是否通顺。
    ///
    /// 与 BagDebugMenu 一样位于 Editor 目录：只在编辑器编译、不参与打包，也无需输入系统。
    /// 所有菜单项依赖运行时的 GameRoot / EquipmentController，需在 Play Mode 下使用。
    /// </summary>
    public static class CombatDebugMenu
    {
        private const string MENU_ROOT = "Tools/战斗调试/";

        // 资产路径与 Assets/ 下的实际文件对应（.asset 按项目策略只本机维护、不入库）
        private const string SAW_ASSET_PATH = "Assets/ScriptableObject/Weapon_Saw.asset";
        private const string GUN_ASSET_PATH = "Assets/ScriptableObject/Weapon_Gun.asset";

        // 与 ItemConfig 表一致：背包里的旧武器已归类为加成道具（Tools/数据库调试/旧武器归类为加成道具）
        // 刀 → 攻击力，弓与箭 → 近战攻速
        private const int ID_IRON_BOW = 1006;       // 铁皮弓
        private const int ID_IRON_KNIFE = 1007;     // 铁刀
        private const int ID_FEATHER_ARROW = 1008;  // 羽毛箭

        // ===== 武器 =====

        [MenuItem(MENU_ROOT + "装备锯子（近战槽）", false, 1)]
        public static void EquipSaw() => PickupWeapon(SAW_ASSET_PATH);

        [MenuItem(MENU_ROOT + "装备枪（主武器槽）", false, 2)]
        public static void EquipGun() => PickupWeapon(GUN_ASSET_PATH);

        // ===== 加成道具 =====

        [MenuItem(MENU_ROOT + "给 1 把铁刀（+攻击力）", false, 11)]
        public static void GiveIronKnife() => GiveItem(ID_IRON_KNIFE);

        [MenuItem(MENU_ROOT + "给 1 把铁皮弓（+攻速）", false, 12)]
        public static void GiveIronBow() => GiveItem(ID_IRON_BOW);

        [MenuItem(MENU_ROOT + "给 1 支羽毛箭（+攻速）", false, 13)]
        public static void GiveFeatherArrow() => GiveItem(ID_FEATHER_ARROW);

        [MenuItem(MENU_ROOT + "使用铁刀", false, 14)]
        public static void UseIronKnife() => UseBonusItem(ID_IRON_KNIFE);

        [MenuItem(MENU_ROOT + "使用铁皮弓", false, 15)]
        public static void UseIronBow() => UseBonusItem(ID_IRON_BOW);

        [MenuItem(MENU_ROOT + "使用羽毛箭", false, 16)]
        public static void UseFeatherArrow() => UseBonusItem(ID_FEATHER_ARROW);

        // ===== 状态 =====

        [MenuItem(MENU_ROOT + "打印战斗状态", false, 21)]
        public static void DumpCombatState()
        {
            if (!TryGetEquipment(out EquipmentController equipment)) return;

            WeaponConfig weapon = equipment.CurrentWeapon;
            string weaponName = weapon == null ? "（空手）" : weapon.weaponName;

            Debug.Log(
                $"[战斗调试] 当前槽位={equipment.CurrentSlot} 武器={weaponName}\n" +
                $"攻击倍率={Blackboard.Get(CombatKeys.AttackMultiplier, 1f):0.##}\n" +
                $"近战攻速倍率={Blackboard.Get(CombatKeys.MeleeAttackSpeedMultiplier, 1f):0.##}\n" +
                $"是否切换中={equipment.IsSwitching} 是否可攻击={equipment.CanAttack}");
        }

        // ===== 内部实现 =====

        private static void PickupWeapon(string assetPath)
        {
            if (!TryGetEquipment(out EquipmentController equipment)) return;

            WeaponConfig config = AssetDatabase.LoadAssetAtPath<WeaponConfig>(assetPath);
            if (config == null)
            {
                Debug.LogError($"[战斗调试] 加载武器配置失败：{assetPath}");
                return;
            }

            equipment.Pickup(config);
            Debug.Log($"[战斗调试] 已装备：{config.weaponName}（槽位 {config.slot}，{SWITCH_HINT}）");
        }

        private static void GiveItem(int itemId)
        {
            if (!TryGetGameRoot(out GameRoot root)) return;

            if (!root.Inventory.AddItem(itemId, 1))
            {
                Debug.LogWarning($"[战斗调试] 添加物品失败 ID:{itemId}（背包已满或物品不存在？）");
                return;
            }

            Debug.Log($"[战斗调试] 已添加物品 ID:{itemId} ×1");
        }

        private static void UseBonusItem(int itemId)
        {
            if (!TryGetGameRoot(out GameRoot root)) return;

            if (root.BonusController == null)
            {
                Debug.LogError("[战斗调试] BonusController 未初始化");
                return;
            }

            if (!root.BonusController.UseItem(itemId))
            {
                Debug.LogWarning($"[战斗调试] 使用失败 ID:{itemId}（数量不足或非加成道具？）");
                return;
            }

            Debug.Log($"[战斗调试] 已使用加成道具 ID:{itemId}，攻击倍率=" +
                      $"{Blackboard.Get(CombatKeys.AttackMultiplier, 1f):0.##}，近战攻速倍率=" +
                      $"{Blackboard.Get(CombatKeys.MeleeAttackSpeedMultiplier, 1f):0.##}");
        }

        private const string SWITCH_HINT = "切换需 0.3 秒";

        private static bool TryGetGameRoot(out GameRoot root)
        {
            root = null;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[战斗调试] 请在 Play Mode 下使用");
                return false;
            }

            root = GameRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[战斗调试] 场景中找不到 GameRoot");
                return false;
            }
            return true;
        }

        private static bool TryGetEquipment(out EquipmentController equipment)
        {
            equipment = null;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[战斗调试] 请在 Play Mode 下使用");
                return false;
            }

            equipment = Object.FindObjectOfType<EquipmentController>(true);
            if (equipment == null)
            {
                Debug.LogError("[战斗调试] 场景中找不到 EquipmentController");
                return false;
            }
            return true;
        }
    }
}
#endif
