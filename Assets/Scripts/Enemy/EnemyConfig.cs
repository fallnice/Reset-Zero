using UnityEngine;
using Combat;

namespace Enemy
{
    /// <summary>
    /// 敌人配置——ScriptableObject，纯数据容器。
    /// 回家在 Unity 中通过 Create > Enemy > Enemy Config 创建资产，挂到 EnemyBrain。
    /// 移动/转身等基础参数仍走 CharacterRoot 的 CharacterConfig，这里只放 AI 决策参数。
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "Enemy/Enemy Config", order = 20)]
    public class EnemyConfig : ScriptableObject
    {
        [Header("感知")]
        [Min(0f)] public float detectionRange = 15f;    // 首次发现目标的距离
        [Min(0f)] public float loseTargetRange = 20f;   // 丢失目标的距离（迟滞，避免边缘抖动）

        [Header("攻击")]
        [Min(0f)] public float attackRange = 2f;         // 进入攻击状态的距离
        [Min(0.01f)] public float attackCooldown = 1.2f; // AI 决策层两次攻击请求的最小间隔

        [Header("导航")]
        [Min(0.1f)] public float obstacleAvoidDistance = 1.5f; // 前方障碍检测距离

        [Header("初始武器")]
        [Tooltip("敌人开局装备的近战武器；为空则敌人无法攻击")]
        public WeaponConfig meleeWeapon;
    }
}
