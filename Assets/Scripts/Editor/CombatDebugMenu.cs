#if UNITY_EDITOR
using System.Text;
using Combat;
using Core;
using Role;
using Role.Controllers;
using Role.Core;
using Role.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

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
            string fireMode = equipment.UsesContinuousAttackInput ? "全自动" : "单次/半自动";
            CharacterRoot character = FindPlayerCharacter();
            string upperBody = character?.upperBodySM == null
                ? "未初始化"
                : $"{character.upperBodySM.CurrentMode}（抑制={character.upperBodySM.IsSuppressed}）";

            CombatStats stats = character != null ? character.Context.CombatStats : null;
            float attackMultiplier = stats != null ? stats.attackMultiplier : 1f;
            float speedMultiplier = stats != null ? stats.meleeAttackSpeedMultiplier : 1f;

            Debug.Log(
                $"[战斗调试] 当前槽位={equipment.CurrentSlot} 武器={weaponName} 模式={fireMode}\n" +
                $"槽位占用：{DescribeSlots(equipment)}\n" +
                $"UpperBody={upperBody}\n" +
                $"攻击倍率={attackMultiplier:0.##}\n" +
                $"近战攻速倍率={speedMultiplier:0.##}\n" +
                $"当前攻击间隔={equipment.CurrentAttackInterval:0.###}s " +
                $"剩余冷却={equipment.AttackCooldownRemaining:0.###}s\n" +
                $"是否切换中={equipment.IsSwitching} 是否可攻击={equipment.CanAttack}\n" +
                $"输入：{DescribeInput(character)}");
        }

        [MenuItem(MENU_ROOT + "输入自检（按 1/2/3 没反应时用）", false, 22)]
        public static void DumpInputState()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[战斗调试] 请在 Play Mode 下使用");
                return;
            }

            CharacterRoot player = FindPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[战斗调试] 场景中找不到受玩家控制的角色");
                return;
            }

            Debug.Log($"[战斗调试·输入] {DescribeInput(player)}");
        }

        /// <summary>
        /// 三个槽位各装了什么。
        ///
        /// 「按 1/2/3 没反应」最常见的根因不是输入失灵，而是目标槽是空的（只弹 Toast、不播换装动画），
        /// 或者手上已经拿着那把武器（同槽切换被忽略）。这两种情况在画面上与输入失灵一模一样，
        /// 所以把槽位占用直接打出来，一眼就能区分。
        /// </summary>
        private static string DescribeSlots(EquipmentController equipment)
        {
            return $"1(主)={SlotLabel(equipment, WeaponSlot.Primary)} " +
                   $"2(副)={SlotLabel(equipment, WeaponSlot.Secondary)} " +
                   $"3(近战)={SlotLabel(equipment, WeaponSlot.Melee)}";
        }

        /// <summary> 单个槽位的显示名；空槽显示「空」，避免与「武器名叫空」混淆 </summary>
        private static string SlotLabel(EquipmentController equipment, WeaponSlot slot)
        {
            WeaponConfig config = equipment.GetWeaponInSlot(slot);
            return config == null ? "（空）" : config.weaponName;
        }

        /// <summary>
        /// 输入链路的现场状态。
        ///
        /// 「按 1/2/3 没反应」只可能是三类原因：① 输入来源为空（组件没挂/被禁用）；
        /// ② 动作集没启用，或生成类里的绑定丢了；③ UI 模态（背包/制作）打开时战斗输入被主动阻断。
        /// 三者在画面上完全一样，所以直接打出来，别再靠猜。
        /// </summary>
        private static string DescribeInput(CharacterRoot character)
        {
            if (character == null) return "玩家角色为空，无法查看输入";

            IInputProvider provider = character.inputProvider;
            if (provider == null)
                return "inputProvider=null ← 没有任何输入来源（角色上挂 PlayerInputProvider 了吗？）";

            StringBuilder sb = new StringBuilder();
            sb.Append(provider.GetType().Name);

            if (provider is MonoBehaviour behaviour)
            {
                sb.Append($"（组件 enabled={behaviour.enabled}，" +
                          $"物体 active={behaviour.gameObject.activeInHierarchy}）");
            }
            else
            {
                sb.Append("（非 MonoBehaviour，无启用状态）");
            }

            sb.Append($" UI模态阻断={character.IsUiInputBlocked}");

            if (provider is PlayerInputProvider playerInput)
                sb.Append($"\n  {DescribeSelectActions(playerInput.Actions)}");
            else
                sb.Append("\n  不是 PlayerInputProvider，无法检查动作绑定");

            return sb.ToString();
        }

        /// <summary> 三个武器槽选择动作的启用状态与绑定路径 </summary>
        private static string DescribeSelectActions(InputActionAsset asset)
        {
            if (asset == null) return "动作集=null（PlayerInputProvider 还没 Awake？）";

            StringBuilder sb = new StringBuilder();
            sb.Append($"动作集 enabled={asset.enabled}");

            AppendAction(sb, asset, "SelectPrimary");
            AppendAction(sb, asset, "SelectSecondary");
            AppendAction(sb, asset, "SelectMelee");

            return sb.ToString();
        }

        /// <summary> 单个动作：是否启用、绑定几条、都绑在哪些控件上 </summary>
        private static void AppendAction(StringBuilder sb, InputActionAsset asset, string actionName)
        {
            InputAction action = asset.FindAction(actionName, false);
            if (action == null)
            {
                sb.Append($"\n  {actionName}=动作不存在（生成的 PlayerInputActions.cs 是旧的）");
                return;
            }

            sb.Append($"\n  {actionName} enabled={action.enabled} 绑定数={action.bindings.Count}");
            for (int i = 0; i < action.bindings.Count; i++)
                sb.Append($" [{action.bindings[i].effectivePath}]");

            // 绑定存在 ≠ 能触发：路径必须解析到真实控件。键盘布局里主键盘数字键的控件名
            // 就是 "1"/"2"/"3"（不是 "digit3"！`Keyboard.current.digit3` 只是 C# 属性名），
            // 小键盘叫 "numpad1"/"numpad2"/"numpad3"。解析不到的绑定运行时永远是死的。
            ReadOnlyArray<InputControl> controls = action.controls;
            sb.Append($" 解析到控件={controls.Count}");
            for (int i = 0; i < controls.Count; i++)
                sb.Append($" <{controls[i].path}>");
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
            Debug.Log($"[战斗调试] 已装备：{config.weaponName}（槽位 {config.slot}，" +
                      $"收武器 {equipment.PutDuration:0.##}s + 掏出 {equipment.TakeDuration:0.##}s）\n" +
                      $"槽位占用：{DescribeSlots(equipment)}");
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

            CharacterRoot character = FindPlayerCharacter();
            CombatStats stats = character != null ? character.Context.CombatStats : null;
            float attackMultiplier = stats != null ? stats.attackMultiplier : 1f;
            float speedMultiplier = stats != null ? stats.meleeAttackSpeedMultiplier : 1f;

            Debug.Log($"[战斗调试] 已使用加成道具 ID:{itemId}，攻击倍率=" +
                      $"{attackMultiplier:0.##}，近战攻速倍率=" +
                      $"{speedMultiplier:0.##}");
        }

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

            CharacterRoot player = FindPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[战斗调试] 场景中找不到受玩家控制的角色（CharacterRoot.isPlayerControlled 全为 false）");
                return false;
            }

            equipment = player.Equipment;
            if (equipment == null)
            {
                Debug.LogError($"[战斗调试] 玩家「{player.name}」上没有 EquipmentController");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 解析玩家角色——**不能**用 `FindObjectOfType&lt;CharacterRoot&gt;(true)`。
        ///
        /// 场景里除真玩家「Player2」外，还有一个敌人靶子「Capsule」和一个**被禁用**的旧「Player」，
        /// 三者都带 CharacterRoot + EquipmentController。上面那种查法会随机命中它们，
        /// 表现为「点了装备、日志也打印成功，但玩家的动画集/武器模型毫无反应」。
        ///
        /// 口径与 GameRoot.ResolvePlayerCombatStats 一致：认 IsPlayerControlled；
        /// 这里额外优先取启用的实例，并在只剩未启用实例时给出提示。
        /// </summary>
        private static CharacterRoot FindPlayerCharacter()
        {
            CharacterRoot[] roots = Object.FindObjectsOfType<CharacterRoot>(true);
            CharacterRoot activePlayer = null;
            CharacterRoot inactivePlayer = null;

            for (int i = 0; i < roots.Length; i++)
            {
                CharacterRoot root = roots[i];
                if (root == null || !root.IsPlayerControlled) continue;

                if (root.gameObject.activeInHierarchy)
                {
                    activePlayer = root;
                    break;
                }

                if (inactivePlayer == null) inactivePlayer = root;
            }

            if (activePlayer != null) return activePlayer;

            if (inactivePlayer != null)
            {
                Debug.LogWarning(
                    $"[战斗调试] 只找到未启用的玩家角色「{inactivePlayer.name}」，请确认场景中的残留对象是否该删");
                return inactivePlayer;
            }

            return null;
        }
    }
}
#endif
