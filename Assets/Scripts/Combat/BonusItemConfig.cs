using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 加成道具配置——ScriptableObject，纯数据容器
    /// 回家在 Unity 中通过 Create > Combat > Bonus Item Config 创建资产，
    /// 放到 Resources/BonusItems/ 目录下，BonusController 启动时自动加载（零拖拽）
    /// </summary>
    [CreateAssetMenu(fileName = "BonusItemConfig", menuName = "Combat/Bonus Item Config", order = 1)]
    public class BonusItemConfig : ScriptableObject
    {
        [Header("关联物品")]
        public int itemId;          // 关联 ItemConfig 表的物品 ID

        [Header("加成数值（百分比，如 15 = +15%）")]
        [Min(0f)] public float attackBonusPercent;              // 攻击力加成
        [Min(0f)] public float meleeAttackSpeedBonusPercent;    // 近战攻速加成
    }
}
