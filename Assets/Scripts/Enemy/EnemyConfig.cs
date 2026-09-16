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
        [Min(0.05f)] public float perceptionSearchInterval = 0.25f; // 无目标时的搜索间隔，避免每帧全场景扫描
        [Range(1f, 360f)] public float viewAngle = 150f; // 视野角（度）；>= 180 视为全向
        [Min(0f)] public float eyeHeight = 1.5f;         // 视线起点相对脚底的高度

        [Header("视线（LOS）")]
        [Tooltip("会遮挡视线的静态几何层；留空则视线检测退化为不被遮挡（仅告警一次）")]
        public LayerMask losObstacleMask;
        [Min(0f)] public float losTargetTolerance = 0.25f; // 允许射线略超过目标，避免目标自身挡住视线

        [Header("听觉（TODO 待战斗层补开火事件）")]
        [Min(0f)] public float hearingRange = 12f;         // 能听到动静的水平半径；0 = 关闭听觉
        [Range(0f, 1f)] public float damageSuspicionBoost = 0.6f;      // 受击时追加的怀疑度
        [Range(0f, 1f)] public float weaponNoiseSuspicionBoost = 0.35f; // 听到武器声追加的怀疑度

        [Header("记忆与怀疑度")]
        [Min(0f)] public float targetMemorySeconds = 4f;      // 最后已知位置的有效记忆时长
        [Range(0f, 1f)] public float investigateSuspicionThreshold = 0.5f; // 进入 Investigate 的怀疑度阈值
        [Min(0f)] public float suspicionDecayPerSecond = 0.25f; // 失去线索后的怀疑度衰减速度

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

        [Header("导航 - 局部避障")]
        [Tooltip("在导航输出与角色输入之间启用 Context Steering；关闭时原方向直通")]
        public bool localAvoidanceEnabled = true;
        [Tooltip("邻近敌人的查询层；必须包含敌人角色所在层，查询后会去重并过滤自己、玩家和死亡角色")]
        public LayerMask avoidanceAgentMask = ~0;
        [Min(0.1f)] public float avoidanceNeighborRadius = 1.8f;
        [Min(0.02f)] public float avoidanceQueryInterval = 0.15f;
        [Min(0f)] public float avoidanceObstacleProbeDistance = 1.2f;
        [Range(0.1f, 1f)] public float avoidanceProbeRadiusFactor = 0.75f;
        [Min(0f)] public float avoidanceInterestWeight = 1f;
        [Min(0f)] public float avoidanceDangerWeight = 2f;
        [Min(0f)] public float avoidanceSeparationWeight = 1.4f;
        [Min(0f)] public float avoidanceTurnPenalty = 0.1f;
        [Min(0f)] public float avoidanceDirectionPersistence = 0.12f;
        [Range(0f, 1f)] public float avoidanceActivationDanger = 0.05f;
        [Tooltip("最高候选分数不超过此值时安全停止，避免强行挤入封闭方向")]
        public float avoidanceBlockedScore = 0.02f;

        [Header("行为（4.1 HFSM）")]
        [Min(0.5f)] public float idleSeconds = 2f;        // 待机多久后开始巡逻（下限避免待机/巡逻高频切换）
        [Min(0.5f)] public float patrolRadius = 8f;       // 巡逻取点半径（以出生点为圆心）
        [Min(0.5f)] public float patrolMinPointDistance = 2f; // 巡逻点与自身的最小距离，避免取到脚下导致瞬间完成
        [Min(1)] public int patrolPickAttempts = 6;       // 每次取点的随机尝试次数，取不到则回到待机
        [Min(0.5f)] public float navigationFailureTimeout = 3f; // 导航连续失败/卡住多久后放弃当前巡逻或归位
        [Min(0.5f)] public float investigateSeconds = 3f; // 到达可疑点后的搜索时长
        [Tooltip("调查时真正贴近可疑点的距离；小于该值会覆盖战斗停止距离(1.5m)，否则敌人会停在墙角另一侧，视线仍被挡")]
        [Min(0.1f)] public float investigateArriveDistance = 0.6f;
        [Tooltip("扫完可疑点后，在其周围额外搜索的点数上限；0 = 只搜可疑点本身")]
        [Min(0)] public int investigateMaxSearchPoints = 2;
        [Min(0.5f)] public float investigateSearchRadius = 3f; // 额外搜索点距可疑点的半径
        [Min(0f)] public float returnHomeDistance = 25f;  // 离出生点超过该距离就放弃追击/搜索，0 = 不限制

        [Header("战术（4.3 Utility）")]
        [Range(0f, 1f)] public float retreatHealthRatio = 0.35f;    // 生命低于此比例强制后撤
        [Range(0f, 1f)] public float threatLevelHealthRatio = 0.35f; // 生命低于此比例视为高威胁
        [Min(0.5f)] public float retreatDurationSeconds = 2.5f;      // 单次撤退最长持续，超时回压上
        [Min(0f)] public float retreatCooldownSeconds = 4f;          // 两次撤退之间的冷却
        [Min(0.5f)] public float flankTimeoutSeconds = 4f;           // 包抄超时，绕不到就放弃
        [Min(0f)] public float tacticalCommitSeconds = 1f;       // 战术保持时长，抑制评分抖动
        [Min(0.5f)] public float flankDistance = 4f;             // 包抄点相对目标的距离
        [Range(10f, 120f)] public float flankAngle = 60f;        // 包抄点相对「目标→自己」的偏转角
        [Min(0f)] public float flankCooldownSeconds = 6f;        // 两次包抄之间的冷却
        [Min(0.1f)] public float holdDistanceFactor = 1.2f;      // 对峙时保持的距离 = attackRange × 该系数

        [Header("调试")]
        [Tooltip("选中敌人时显示 AI 调试 Gizmos：视野锥、巡逻点、战术点与怀疑目标")]
        public bool debugDrawState = true;

        [Header("初始武器")]
        [Tooltip("敌人开局装备的近战武器；为空则敌人无法攻击")]
        public WeaponConfig meleeWeapon;

        private void OnValidate()
        {
            // 丢失距离必须大于发现距离，否则迟滞退化成「刚发现就丢失」，敌人会在 Idle 与 Chase 之间抖动
            if (loseTargetRange < detectionRange)
                loseTargetRange = detectionRange;
        }
    }
}
