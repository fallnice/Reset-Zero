using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 武器配置——ScriptableObject，纯数据容器
    /// 回家在 Unity 中通过 Create > Combat > Weapon Config 创建资产（锯子/枪各一份）
    /// 装备控制器只持有引用不存数值，不同武器可复用
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "Combat/Weapon Config", order = 0)]
    public class WeaponConfig : ScriptableObject
    {
        [Header("基础")]
        public string weaponName;
        public WeaponType type;
        public WeaponSlot slot;

        [Header("数值")]
        [Min(0f)] public float damage;
        [Min(0f)] public float range;   // 近战=攻击半径，枪械=射程
        [Min(0.01f)] public float attackInterval = 0.5f; // 两次有效攻击之间的基础间隔（秒）

        [Header("远程武器")]
        [Tooltip("勾选后按住攻击键会按 attackInterval 连续射击；近战武器忽略此项")]
        public bool isAutomatic;

        [Header("表现层（回家后填）")]
        public GameObject modelPrefab;  // 武器模型，挂到右手挂点
        public int animPoseParam;       // Animator 姿态参数值（0=空手 1=近战 2=枪械）
    }
}
