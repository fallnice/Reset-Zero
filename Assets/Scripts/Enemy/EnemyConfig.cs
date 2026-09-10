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
        [Min(0.05f)] public float perceptionSearchInterval = 0.25f; // 无目标时的搜索间隔，避免每帧 FindObjects 分配

        [Header("攻击")]
        [Min(0f)] public float attackRange = 2f;         // 进入攻击状态的距离
        [Min(0.01f)] public float attackCooldown = 1.2f; // AI 决策层两次攻击请求的最小间隔

        [Header("导航 - 通用")]
        [Min(0.1f)] public float obstacleAvoidDistance = 1.5f; // DirectNavigation 前方障碍检测距离
        [Tooltip("DirectNavigation 只检测这些障碍层；应排除 Player/Enemy/Trigger")]
        public LayerMask navigationObstacleMask;
        [Min(0.05f)] public float navigationStoppingDistance = 1.5f;

        [Header("导航 - A* 重算")]
        [Min(0.05f)] public float repathInterval = 0.35f;
        [Min(0.05f)] public float failedRetryInterval = 1f;
        [Min(0.05f)] public float targetMoveThreshold = 0.75f;
        [Min(0.05f)] public float waypointReachDistance = 0.3f;
        [Min(1)] public int maxSearchNodes = 4096;

        [Header("导航 - 卡住诊断")]
        [Min(0.05f)] public float stuckSampleInterval = 0.5f;
        [Min(0.01f)] public float stuckMinProgress = 0.1f;
        [Min(0.1f)] public float stuckTimeout = 1.5f;

        [Header("初始武器")]
        [Tooltip("敌人开局装备的近战武器；为空则敌人无法攻击")]
        public WeaponConfig meleeWeapon;
    }
}
