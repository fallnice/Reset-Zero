using Combat;
using Core;
using Role.Core;
using System.Collections.Generic;
using UnityEngine;

namespace Controller
{
    /// <summary>
    /// 加成道具控制器——处理「使用」逻辑
    /// 使用流程：查加成配置 → 扣 1 个道具 → 把加成累加到角色 Blackboard → 广播事件
    /// UI 层（BagView 的使用按钮）回家后调用 UseItem(itemId) 即可
    /// </summary>
    public class BonusController
    {
        private IInventory _inventory;
        private readonly Dictionary<int, BonusItemConfig> _bonusDict = new Dictionary<int, BonusItemConfig>();

        public void Init(IInventory inventory)
        {
            _inventory = inventory;
            _bonusDict.Clear();

            // 从 Resources/BonusItems/ 加载所有加成道具配置（回家把资产放该目录，零拖拽）
            BonusItemConfig[] configs = Resources.LoadAll<BonusItemConfig>("BonusItems");
            foreach (BonusItemConfig config in configs)
            {
                if (config == null || config.itemId <= 0) continue;
                if (!_bonusDict.ContainsKey(config.itemId))
                    _bonusDict.Add(config.itemId, config);
            }
        }

        /// <summary> 该物品是否为加成道具（供 UI 判断是否显示「使用」按钮） </summary>
        public bool IsBonusItem(int itemId)
        {
            return _bonusDict.ContainsKey(itemId);
        }

        /// <summary> 使用一个加成道具：扣道具 + 应用加成，成功返回 true </summary>
        public bool UseItem(int itemId)
        {
            if (!_bonusDict.TryGetValue(itemId, out BonusItemConfig config))
            {
                Debug.LogWarning($"[BonusController] 物品 ID:{itemId} 不是加成道具");
                return false;
            }

            if (_inventory == null || !_inventory.RemoveItem(itemId, 1))
            {
                Debug.LogWarning($"[BonusController] 使用失败：物品不足 ID:{itemId}");
                return false;
            }

            ApplyBonus(config);

            EventBus.Emit(EventName.Bonus_Used, itemId);
            return true;
        }

        private static void ApplyBonus(BonusItemConfig config)
        {
            if (config.attackBonusPercent > 0f)
            {
                float current = Blackboard.Get(CombatKeys.AttackMultiplier, 1f);
                Blackboard.Set(CombatKeys.AttackMultiplier, current + config.attackBonusPercent / 100f);
            }

            if (config.meleeAttackSpeedBonusPercent > 0f)
            {
                float current = Blackboard.Get(CombatKeys.MeleeAttackSpeedMultiplier, 1f);
                Blackboard.Set(CombatKeys.MeleeAttackSpeedMultiplier, current + config.meleeAttackSpeedBonusPercent / 100f);
            }
        }
    }
}
