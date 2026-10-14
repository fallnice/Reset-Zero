using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Combat;
using Controller;
using Core;
using Dao;
using Enemy;
using Enemy.Navigation;
using Interaction;
using Loot;
using Model;
using Role;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// PlayMode 冒烟自检（4.5 局部避障的行为验收用）。
    ///
    /// 背景：4.5 的验收项（错开、让行、不指墙内、死路阻塞、零 GC、关闭后回归）大多只在
    /// 运行期才暴露，靠人进编辑器肉眼看成本高且不可回归。本工具让 Unity 无头进 PlayMode，
    /// 自动跑「基线 → 追击」两段采样，把可观测的量（位移、距离、避障状态、邻居数、阻塞占比、
    /// 托管堆变化）写成结果文件，形成和 SceneAudit 一样的「跑 → 读结果 → 修 → 再跑」闭环。
    ///
    /// 用法（**命令行不要加 -quit**：本方法自己调用 EditorApplication.Exit）：
    ///   Unity.exe -batchmode -nographics -projectPath <工程> \
    ///            -executeMethod EditorTools.PlayModeSmokeCheck.RunFromCommandLine -logFile <log>
    ///
    /// 结果文件：默认 %TEMP%/unity_playmode_audit.log
    ///   - 逐项 [PASS]/[FAIL]，[INFO] 为只观察不判定
    ///   - 末尾 PLAYMODE_AUDIT_RESULT: PASS / FAIL
    ///   - 环境变量：PLAYMODE_AUDIT_LOG（结果路径）、PLAYMODE_AUDIT_SCENE（指定场景）
    ///
    /// 判定口径（先粗后细，避免误杀）：
    ///   - 位移/接近/阻塞占比做硬判定；
    ///   - GC 只记录数值做 [INFO]：编辑器自身也在分配，绝对值不能作为「避障零 GC」的证据。
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeSmokeCheck
    {
        public const string ResultMarkerPass = "PLAYMODE_AUDIT_RESULT: PASS";
        public const string ResultMarkerFail = "PLAYMODE_AUDIT_RESULT: FAIL";

        // 进出 PlayMode 会触发域重载，静态字段全被清空、事件订阅也会丢。
        // 状态必须落在 SessionState（同一编辑器会话内跨域重载保留），并在静态构造里续上。
        private const string KeyRunning = "PlayModeSmokeCheck.Running";
        private const string KeyPhase = "PlayModeSmokeCheck.Phase";
        private const string KeyStart = "PlayModeSmokeCheck.StartTime";
        private const string KeyPhaseStart = "PlayModeSmokeCheck.PhaseStart";
        private const string KeyScene = "PlayModeSmokeCheck.Scene";
        private const string KeyResult = "PlayModeSmokeCheck.Result";
        private const string KeyBagSnapshot = "PlayModeSmokeCheck.BagSnapshot";

        private const string DefaultLogFileName = "unity_playmode_audit.log";
        private const string SceneEnvVar = "PLAYMODE_AUDIT_SCENE";
        private const string LogEnvVar = "PLAYMODE_AUDIT_LOG";

        // 采样节奏：进 PlayMode 后先静置，再跑基线，然后把玩家挪到敌人附近触发追击
        private const float SettleSeconds = 1.5f;
        private const float BaselineSeconds = 3.0f;
        private const float ChaseSeconds = 15.0f;   // 敌人从各自防区走到玩家身边需要时间
        private const float MaxTotalSeconds = 240.0f;

        // 判定阈值
        private const float MinBaselineMove = 0.5f;     // 基线阶段平均位移（米）
        private const float MinApproach = 1.0f;         // 追击阶段至少要缩短的距离（米）
        private const float MaxBlockedRatio = 0.4f;     // 避障阻塞帧占比上限
        private const float MinPairDistance = 0.35f;    // 敌人两两最小间距（过小=挤在一起）
        private const float MinWindupSeconds = 0.05f;   // 起手到结算的最小间隔（小于它=没有前摇，仍是即时命中）
        private const int QuestRewardItemId = 1001;     // 任务奖励物品（用于核对交付后入包）

        private sealed class EnemySample
        {
            public EnemyBrain Brain;
            public CharacterRoot Root;
            public Vector3 LastPosition;
            public float MovedDistance;
            public float StartDistanceToPlayer = -1f;
            public float MinDistanceToPlayer = float.MaxValue;
            public int Frames;
            public int BlockedFrames;
            public int AdjustingFrames;
            public int SaturatedFrames;
            public int NeighborSum;
            public bool SawChase;
            public bool SawSharedAlert;
            public long LastAlertSequence;
            public int NavFailedFrames;                         // 导航处于失败状态的帧数
            public EnemyNavigationFailure LastNavFailure;       // 最近一次导航失败原因
            public float MoveDirSum;                            // 输出方向模长累计（0=一直在原地磨）
            public float EndDistanceToPlayer = -1f;             // 追击阶段结束时与玩家的距离
            public bool KilledBySmokeCheck;                     // 本轮是否由冒烟清场/取消流程击杀
        }

        private static string _resultPath;
        private static string _scenePath;
        private static float _startTime;
        private static float _phaseStart;
        private static int _phase;                       // 0 打开场景 1 等待进入 2 基线 3 追击 4 退出 99 结束
        private static bool _subscribed;

        private static readonly List<EnemySample> _samples = new List<EnemySample>();
        private static readonly List<string> _errors = new List<string>();

        private static CharacterRoot _player;
        private static long _gcAtChaseStart;
        private static long _gcAtChaseEnd;
        private static float _minPairDistance = float.MaxValue;
        private static int _navFailWarnings;   // 运行期 A* 报 NoProgress 的次数
        private static Vector3 _playerPlannedPosition;   // 计划把玩家放到的位置（校验瞬移是否生效）

        // ── 攻击观察（09-18 批次：前摇 / 命中帧 / 取消兜底） ──
        private static int _attackStarts;       // 敌人攻击起手次数（EquipmentController.AttackCommitted）
        private static int _damageEvents;       // 玩家受伤次数（HealthController.Damaged）
        private static float _firstAttackTime = -1f;
        private static float _firstDamageTime = -1f;

        // 取消测试：清场后只留一个敌人，它一起手就击杀，看迟到的伤害会不会补上
        private const float CancelSettleSeconds = 1.0f;        // 清场后等其它敌人死透
        private const float CancelWaitAttackSeconds = 10.0f;   // 等目标起手
        private const float CancelObserveSeconds = 2.5f;       // 起手后观察窗口（须 > 兜底 0.45s）
        private const float CancelKillDamage = 9999f;
        private static int _cancelStage;                       // 0 清场 1 等起手 2 观察
        private static float _cancelStageStart;
        private static bool _cancelBaselineSet;
        private static float _cancelBaseline;
        private static EnemySample _cancelTarget;
        private static float _cancelLastAttackTry;
        private static string _cancelResult;

        // ── 死亡掉落与拾取（10-08 批次：击杀 → 掉落 → 拾取 → 入包） ──
        private static int _lootStage;          // 0 选择本轮掉落 1 交互拾取 2 校验背包 3 制造
        private static float _lootStageStart;
        private static int _lootItemId = -1;
        private static int _lootItemCount;
        private static int _bagBefore = -1;
        private static int _bagAfter = -1;
        private static int _pickupCompletedCount;
        private static PickupItem _selectedLootPickup;
        private static RecipeInfo _selectedLootRecipe;
        private static EventBus.SubscriptionToken _lootDroppedToken;
        private static EventBus.SubscriptionToken _lootSpawnToken;
        private static EventBus.SubscriptionToken _pickupCompletedToken;
        private static readonly HashSet<CharacterRoot> _diedLootVictims = new HashSet<CharacterRoot>();
        private static readonly List<CharacterRoot> _pendingLootVictims = new List<CharacterRoot>();
        private static readonly List<PickupItem> _pendingLootPickups = new List<PickupItem>();
        private static readonly List<PickupItem> _spawnedLootPickups = new List<PickupItem>();
        private static readonly List<BagSlotInfo> _bagSnapshot = new List<BagSlotInfo>();
        private static bool _bagSnapshotCaptured;
        private static bool _bagSnapshotRestored;
        private static bool _bagSnapshotRecoveredAfterReload;
        private static bool _bagSnapshotCorrupted;
        private static float _snapshotRestoreWaitStart;
        private static string _lootResult;
        private static string _craftResult;      // 制造链（配方来自数据库 CraftMain / CraftSecond）

        // ── 任务链路（10-12 任务系统：接取 → 目标事件 → 交付） ──
        private static int _questStage;          // 0 记录奖励基线 1 交付 2 校验
        private static float _questStageStart;
        private static string _questAcceptResult;
        private static string _questTurnInResult;
        private static int _questRewardBefore = -1;
        private static int _questRewardAfter = -1;

        static PlayModeSmokeCheck()
        {
            // 域重载后（进入 PlayMode 的那一刻）由 [InitializeOnLoad] 触发，把流程续上
            if (!SessionState.GetBool(KeyRunning, false)) return;
            _scenePath = SessionState.GetString(KeyScene, "");
            _resultPath = SessionState.GetString(KeyResult, "");
            _startTime = SessionState.GetFloat(KeyStart, 0f);
            _phaseStart = SessionState.GetFloat(KeyPhaseStart, 0f);
            _phase = SessionState.GetInt(KeyPhase, 0);
            LoadBagSnapshotFromSession();
            _subscribed = true;
            Application.logMessageReceived += HandleLog;
            EditorApplication.update += Tick;
            SubscribeLootEvents();
        }

        /// <summary>
        /// 命令行入口（-executeMethod）。跑完写结果文件并按结果退出：0=通过，1=存在失败项。
        /// 仅用于批处理：不要在编辑器菜单里调用（会直接关闭编辑器）。
        /// </summary>
        public static void RunFromCommandLine()
        {
            _resultPath = Environment.GetEnvironmentVariable(LogEnvVar);
            if (string.IsNullOrEmpty(_resultPath))
                _resultPath = Path.Combine(Path.GetTempPath(), DefaultLogFileName);

            _scenePath = Environment.GetEnvironmentVariable(SceneEnvVar);
            if (string.IsNullOrEmpty(_scenePath))
            {
                string dir = Path.Combine(Application.dataPath, "Scenes");
                if (Directory.Exists(dir))
                {
                    string[] scenes = Directory.GetFiles(dir, "*.unity");
                    if (scenes.Length > 0)
                        _scenePath = "Assets/Scenes/" + Path.GetFileName(scenes[0]);
                }
            }

            _startTime = (float)EditorApplication.timeSinceStartup;
            SessionState.SetString(KeyScene, _scenePath ?? "");
            SessionState.SetString(KeyResult, _resultPath ?? "");
            SessionState.SetFloat(KeyStart, _startTime);
            SessionState.SetFloat(KeyPhaseStart, _startTime);
            SessionState.SetInt(KeyPhase, 0);
            SessionState.SetBool(KeyRunning, true);
            ResetLootState();
            SubscribeLootEvents();

            if (!_subscribed)
            {
                Application.logMessageReceived += HandleLog;
                EditorApplication.update += Tick;
                _subscribed = true;
            }
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition != null && condition.Contains("NoProgress"))
                _navFailWarnings++;

            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            // 无头（-batchmode -nographics）下 UI 初始化会报「没有图形设备」，
            // 这是环境噪声、不是逻辑错误（实机有图形设备），不能让它把验收判成失败
            if (condition != null && condition.Contains("No graphic device is available")) return;

            if (_errors.Count < 20) _errors.Add(type + ": " + condition);
        }

        /// <summary> 清空本轮掉落、拾取和库存恢复状态。 </summary>
        private static void ResetLootState()
        {
            _lootStage = 0;
            _lootItemId = -1;
            _lootItemCount = 0;
            _bagBefore = -1;
            _bagAfter = -1;
            _pickupCompletedCount = 0;
            _selectedLootPickup = null;
            _selectedLootRecipe = null;
            _diedLootVictims.Clear();
            _pendingLootVictims.Clear();
            _pendingLootPickups.Clear();
            _spawnedLootPickups.Clear();
            _bagSnapshot.Clear();
            _bagSnapshotCaptured = false;
            _bagSnapshotRestored = false;
            _bagSnapshotRecoveredAfterReload = false;
            _bagSnapshotCorrupted = false;
            _snapshotRestoreWaitStart = 0f;
            SessionState.EraseString(KeyBagSnapshot);
            _lootResult = null;
            _craftResult = null;
        }

        /// <summary> 订阅本轮掉落生成与拾取完成事件，只接受 EnemyLootDrop 产生的实例。 </summary>
        private static void SubscribeLootEvents()
        {
            _lootDroppedToken?.Dispose();
            _lootSpawnToken?.Dispose();
            _pickupCompletedToken?.Dispose();
            _lootDroppedToken = EventBus.Subscribe(EventName.Loot_Dropped, HandleLootDropped);
            _lootSpawnToken = EventBus.Subscribe(EventName.Loot_ItemSpawned, HandleLootItemSpawned);
            _pickupCompletedToken = EventBus.Subscribe(EventName.Pickup_Completed, HandlePickupCompleted);
        }

        /// <summary> 记录本轮由冒烟击杀阶段触发掉落结算的受害角色。 </summary>
        private static void HandleLootDropped(object[] args)
        {
            if (args == null || args.Length < 3 || !(args[0] is CharacterRoot victim) || victim == null) return;
            for (int i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].Root == victim && _samples[i].KilledBySmokeCheck)
                {
                    _diedLootVictims.Add(victim);
                    for (int pendingIndex = _pendingLootVictims.Count - 1; pendingIndex >= 0; pendingIndex--)
                    {
                        if (_pendingLootVictims[pendingIndex] != victim) continue;
                        PickupItem pickup = _pendingLootPickups[pendingIndex];
                        if (pickup != null && !_spawnedLootPickups.Contains(pickup))
                            _spawnedLootPickups.Add(pickup);
                        _pendingLootVictims.RemoveAt(pendingIndex);
                        _pendingLootPickups.RemoveAt(pendingIndex);
                    }
                    return;
                }
            }
        }

        /// <summary> 仅记录已确认受害角色在本轮死亡链实际生成的拾取实例。 </summary>
        private static void HandleLootItemSpawned(object[] args)
        {
            if (args == null || args.Length < 4 || !(args[0] is CharacterRoot victim)
                || !(args[1] is PickupItem pickup) || pickup == null)
                return;
            if (_diedLootVictims.Contains(victim))
            {
                if (!_spawnedLootPickups.Contains(pickup))
                    _spawnedLootPickups.Add(pickup);
                return;
            }
            _pendingLootVictims.Add(victim);
            _pendingLootPickups.Add(pickup);
        }

        /// <summary> 仅统计当前选中掉落物对应的拾取完成事件。 </summary>
        private static void HandlePickupCompleted(object[] args)
        {
            if (args == null || args.Length < 3 || !(args[0] is PickupItem pickup)
                || !(args[1] is int itemId) || !(args[2] is int count))
                return;
            if (pickup == _selectedLootPickup && itemId == _lootItemId && count == _lootItemCount)
                _pickupCompletedCount++;
        }

        // ── 状态机（EditorApplication.update 在编辑态与运行态都会触发） ──────────

        private static void Tick()
        {
            if (_phase == 99) return;

            // 时间基准必须用 EditorApplication.timeSinceStartup：Time.realtimeSinceStartup
            // 会在进入 PlayMode 时归零，跨阶段计时会算错。
            double now = EditorApplication.timeSinceStartup;

            if (now - _startTime > MaxTotalSeconds)
            {
                // 库存已变更时优先等待依赖恢复，不能让总超时绕过存档回滚。
                if (_bagSnapshotRecoveredAfterReload && !_bagSnapshotCorrupted)
                {
                    TickLoot(now);
                    return;
                }
                Finish("总时长超过 " + MaxTotalSeconds.ToString("F0") + " 秒，强制收尾（结果可能不完整）");
                return;
            }

            switch (_phase)
            {
                case 0:
                    if (string.IsNullOrEmpty(_scenePath))
                    {
                        Finish("未找到可运行的场景（Assets/Scenes 下没有 .unity，或用 PLAYMODE_AUDIT_SCENE 指定）");
                        return;
                    }
                    EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);
                    SetPhase(1, now);
                    EditorApplication.EnterPlaymode();
                    break;

                case 1:
                    if (!(EditorApplication.isPlaying && !EditorApplication.isPaused)) break;
                    if (now - _phaseStart < SettleSeconds) break;
                    CollectActors();
                    SetPhase(2, now);
                    break;

                case 2:
                    SampleFrame();
                    if (now - _phaseStart < BaselineSeconds) break;
                    SetupChase();
                    _gcAtChaseStart = GC.GetTotalMemory(false);
                    SetPhase(3, now);
                    break;

                case 3:
                    SampleFrame();
                    if (now - _phaseStart < ChaseSeconds) break;
                    _gcAtChaseEnd = GC.GetTotalMemory(false);
                    SetPhase(4, now);
                    break;

                case 4:
                    TickCancelTest(now);
                    break;

                case 5:
                    TickLoot(now);
                    break;

                case 6:
                    TickQuest(now);
                    break;
            }
        }

        /// <summary> 任务交付：本轮已经历击杀、拾取与制造，目标应已达成，可交付领奖。 </summary>
        private static void TickQuest(double now)
        {
            QuestController controller =
                GameRoot.Instance != null ? GameRoot.Instance.QuestController : null;
            IInventory bag = GameRoot.Instance != null ? GameRoot.Instance.Inventory : null;

            switch (_questStage)
            {
                case 0:
                    if (controller == null || bag == null)
                    {
                        _questTurnInResult = "SKIP 拿不到任务控制器或背包";
                        Finish(null);
                        return;
                    }
                    _questRewardBefore = bag.GetItemTotalCount(QuestRewardItemId);
                    _questStageStart = (float)now;
                    _questStage = 1;
                    break;

                case 1:
                    if (now - _questStageStart < 0.6f) break;
                    if (!controller.IsReadyToTurnIn)
                    {
                        _questTurnInResult = "INFO 目标未全部达成（状态=" + controller.State
                            + "，进度[收集/制造/击杀]=" + string.Join("/", controller.Progress)
                            + "），本轮不做交付";
                        Finish(null);
                        return;
                    }
                    controller.TryTurnIn();
                    _questStageStart = (float)now;
                    _questStage = 2;
                    break;

                case 2:
                    if (now - _questStageStart < 0.6f) break;
                    _questRewardAfter = bag.GetItemTotalCount(QuestRewardItemId);
                    _questTurnInResult = controller.State == QuestState.Completed
                        ? "PASS 交付完成，状态=" + controller.State
                          + "，奖励 " + QuestRewardItemId + " 数量 "
                          + _questRewardBefore + " → " + _questRewardAfter
                        : "FAIL 交付未完成，状态=" + controller.State;
                    Finish(null);
                    break;
            }
        }

        /// <summary>
        /// 取消测试：清场只留一个敌人，它一起手就击杀，观察窗口内玩家不应再掉血
        /// （验证「前摇中死亡/眩晕会取消待结算攻击，迟到的事件不补伤害」）。
        /// 报告同样要在退出 PlayMode 之前写完，所以这里直接收尾。
        /// </summary>
        private static void TickCancelTest(double now)
        {
            switch (_cancelStage)
            {
                case 0:
                    // 只留一个活着的敌人，其余清场，避免其它人的伤害污染判定
                    _cancelTarget = null;
                    for (int i = 0; i < _samples.Count; i++)
                    {
                        EnemySample s = _samples[i];
                        bool alive = s.Root != null && s.Root.Health != null
                            && s.Root.Health.CurrentHealth > 0f;
                        if (_cancelTarget == null && alive)
                        {
                            _cancelTarget = s;
                            continue;
                        }
                        Kill(s);
                    }

                    // 摆位：把玩家挪到目标正前方（进入攻击距离），否则它可能一直在巡逻不发起攻击。
                    // 瞬移同样要先禁用 CharacterController，并顺带让它面朝玩家。
                    if (_cancelTarget != null && _player != null)
                    {
                        Transform t = _cancelTarget.Brain.transform;
                        CharacterController cc = _player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        _player.transform.position = GroundPoint(t.position + t.forward * 1.2f);
                        if (cc != null) cc.enabled = true;

                        Vector3 facing = _player.transform.position - t.position;
                        facing.y = 0f;
                        if (facing.sqrMagnitude > 0.0001f)
                            t.rotation = Quaternion.LookRotation(facing);
                    }

                    _cancelStageStart = (float)now;
                    _cancelStage = 1;
                    break;

                case 1:
                    if (now - _cancelStageStart < CancelSettleSeconds) break;
                    if (!_cancelBaselineSet)
                    {
                        _cancelBaseline = _player != null && _player.Health != null
                            ? _player.Health.CurrentHealth : 0f;
                        _cancelBaselineSet = true;
                        break;
                    }
                    // 等它自然起手太不稳定（可能在巡逻、可能刚进冷却），这里主动提交攻击请求；
                    // 一旦起手，HandleEnemyAttackCommitted 会立刻击杀它并进入观察阶段
                    if (_cancelTarget != null && _cancelTarget.Root != null
                        && _cancelTarget.Root.Equipment != null
                        && now - _cancelLastAttackTry > 0.5f)
                    {
                        _cancelLastAttackTry = (float)now;
                        _cancelTarget.Root.Equipment.Attack(0.45f);
                    }

                    if (now - _cancelStageStart > CancelSettleSeconds + CancelWaitAttackSeconds)
                    {
                        _cancelResult = "SKIP 目标在 " + CancelWaitAttackSeconds.ToString("F0")
                            + "s 内没有起手，本轮未触发取消测试";
                        SetPhase(5, now);
                    }
                    break;

                case 2:
                    if (now - _cancelStageStart < CancelObserveSeconds) break;
                    float hp = _player != null && _player.Health != null ? _player.Health.CurrentHealth : 0f;
                    _cancelResult = hp < _cancelBaseline - 0.001f
                        ? "FAIL 起手后击杀攻击者，玩家仍掉了 " + (_cancelBaseline - hp).ToString("F1")
                          + " 血（迟到的伤害被补上了）"
                        : "PASS 起手后击杀攻击者，观察 " + CancelObserveSeconds.ToString("F1")
                          + "s 玩家血量未再变化（本次伤害已取消）";
                    SetPhase(5, now);
                    break;
            }
        }

        /// <summary> 只验证本轮死亡生成的掉落，并要求该材料参与一次精确制造。 </summary>
        private static void TickLoot(double now)
        {
            if (_bagSnapshotCorrupted)
            {
                FailLootCheck("跨域库存快照损坏，无法安全恢复测试存档");
                SetPhase(6, now);
                return;
            }
            if (_bagSnapshotRecoveredAfterReload)
            {
                if (_snapshotRestoreWaitStart <= 0f)
                    _snapshotRestoreWaitStart = (float)now;
                bool databaseReady = SqliteManager.Instance != null && SqliteManager.Instance.IsReady;
                if (!databaseReady && now - _snapshotRestoreWaitStart < 10f) return;

                FailLootCheck(databaseReady
                    ? "库存变更期间发生域重载，运行态关联已丢失"
                    : "域重载后等待数据库恢复超时");
                if (databaseReady) RestoreBagSnapshot();
                SetPhase(6, now);
                return;
            }

            switch (_lootStage)
            {
                case 0:
                    if (!TryPrepareLootCheck(out string prepareReason))
                    {
                        FailLootCheck(prepareReason);
                        SetPhase(6, now);
                        break;
                    }
                    _selectedLootPickup.OnInteract(_player.gameObject);
                    _lootStageStart = (float)now;
                    _lootStage = 1;
                    break;

                case 1:
                    if (now - _lootStageStart < 0.8f) break;
                    ValidatePickupAndCraft();
                    SetPhase(6, now);
                    break;
            }
        }

        /// <summary> 选择本轮生成且能参与现有配方的拾取物，并记录拾取前状态。 </summary>
        private static bool TryPrepareLootCheck(out string reason)
        {
            reason = null;
            IInventory inventory = GameRoot.Instance != null ? GameRoot.Instance.Inventory : null;
            if (_player == null || inventory == null || SqliteManager.Instance == null
                || !SqliteManager.Instance.IsReady)
            {
                reason = "玩家、背包或数据库未就绪";
                return false;
            }
            if (!TrySelectCraftableLoot(inventory, out _selectedLootPickup, out _selectedLootRecipe))
            {
                reason = "本轮死亡未生成可用于现有配方的掉落物，或容量不足以完成制造";
                return false;
            }

            _lootItemId = ReadPickupInt(_selectedLootPickup, "itemId");
            _lootItemCount = ReadPickupInt(_selectedLootPickup, "count");
            if (_lootItemId <= 0 || _lootItemCount <= 0)
            {
                reason = "本轮掉落物配置非法";
                return false;
            }

            CaptureBagSnapshot(inventory);
            _bagBefore = inventory.GetItemTotalCount(_lootItemId);
            _craftResult = "待验证配方 " + _selectedLootRecipe.RecipeId;
            return true;
        }

        /// <summary> 精确验证拾取结果、材料消耗和成品增量。 </summary>
        private static void ValidatePickupAndCraft()
        {
            IInventory inventory = GameRoot.Instance != null ? GameRoot.Instance.Inventory : null;
            _bagAfter = inventory != null ? inventory.GetItemTotalCount(_lootItemId) : -1;
            bool destroyed = _selectedLootPickup == null;
            bool exactIncrease = _bagBefore >= 0 && _bagAfter - _bagBefore == _lootItemCount;
            if (!destroyed || !exactIncrease || _pickupCompletedCount != 1)
            {
                FailLootCheck("拾取结果不精确 destroyed=" + destroyed + " before=" + _bagBefore
                    + " after=" + _bagAfter + " expected=" + _lootItemCount
                    + " eventCount=" + _pickupCompletedCount);
                RestoreBagSnapshot();
                return;
            }

            _craftResult = TryCraftUsingLoot(inventory, _lootItemId);
            _lootResult = _craftResult.StartsWith("PASS", StringComparison.Ordinal)
                ? "PASS itemId=" + _lootItemId + " 入包=" + _lootItemCount + " eventCount=1"
                : "FAIL " + _craftResult;
            RestoreBagSnapshot();
        }

        /// <summary> 从本轮生成实例中选择至少有一个配方会消耗的掉落物。 </summary>
        private static bool TrySelectCraftableLoot(
            IInventory inventory,
            out PickupItem selectedPickup,
            out RecipeInfo selectedRecipe)
        {
            selectedPickup = null;
            selectedRecipe = null;
            Dictionary<int, RecipeInfo> recipes = new RecipeDao().GetAllRecipes();
            for (int pickupIndex = 0; pickupIndex < _spawnedLootPickups.Count; pickupIndex++)
            {
                PickupItem pickup = _spawnedLootPickups[pickupIndex];
                if (pickup == null || !pickup.CanInteract) continue;
                int itemId = ReadPickupInt(pickup, "itemId");

                foreach (KeyValuePair<int, RecipeInfo> pair in recipes)
                {
                    RecipeInfo recipe = pair.Value;
                    if (recipe?.Materials == null || !recipe.Materials.ContainsKey(itemId)) continue;
                    int pickupCount = ReadPickupInt(pickup, "count");
                    if (!CanPrepareRecipe(inventory, recipe, itemId, pickupCount)) continue;
                    selectedPickup = pickup;
                    selectedRecipe = recipe;
                    return true;
                }
            }
            return false;
        }

        /// <summary> 在背包副本中按真实顺序模拟拾取、补料和制造，确认容量与数量均可满足。 </summary>
        private static bool CanPrepareRecipe(
            IInventory inventory,
            RecipeInfo recipe,
            int lootItemId,
            int lootItemCount)
        {
            List<BagSlotInfo> simulated = CloneBagSlots();
            if (!TryAddToSlots(simulated, lootItemId, lootItemCount)) return false;

            foreach (KeyValuePair<int, int> material in recipe.Materials)
            {
                if (material.Key == lootItemId) continue;
                int lack = material.Value - GetSlotTotal(simulated, material.Key);
                if (lack > 0 && !TryAddToSlots(simulated, material.Key, lack)) return false;
            }

            foreach (KeyValuePair<int, int> material in recipe.Materials)
            {
                if (!TryRemoveFromSlots(simulated, material.Key, material.Value)) return false;
            }
            return TryAddToSlots(simulated, recipe.ResultItemId, recipe.ResultCount);
        }

        /// <summary> 补齐非掉落材料后制造，并验证成品和所有材料的精确差值。 </summary>
        private static string TryCraftUsingLoot(IInventory inventory, int lootItemId)
        {
            RecipeInfo recipe = _selectedLootRecipe;
            if (recipe?.Materials == null || !recipe.Materials.ContainsKey(lootItemId))
                return "FAIL 准备阶段配方丢失或不消耗本轮掉落";

            foreach (KeyValuePair<int, int> material in recipe.Materials)
            {
                if (material.Key == lootItemId) continue;
                int lack = material.Value - inventory.GetItemTotalCount(material.Key);
                if (lack > 0 && !inventory.AddItem(material.Key, lack))
                    return "FAIL 补充非掉落材料失败 itemId=" + material.Key;
            }

            var materialBefore = new Dictionary<int, int>();
            foreach (KeyValuePair<int, int> material in recipe.Materials)
                materialBefore[material.Key] = inventory.GetItemTotalCount(material.Key);
            if (materialBefore[lootItemId] < recipe.Materials[lootItemId])
                return "FAIL 本轮拾取材料不足 itemId=" + lootItemId;

            int resultBefore = inventory.GetItemTotalCount(recipe.ResultItemId);

            // 必须走用例层：`Craft_Success` 领域事件是由 `CraftController.DoCraft` 发出的，
            // 直接调 `IInventory.TryCraft` 不发事件 → 任务的「制造」目标统计不到，交付就永远差一项。
            string failReason = null;
            CraftController craftController =
                GameRoot.Instance != null ? GameRoot.Instance.CraftController : null;
            bool crafted;
            if (craftController != null)
            {
                crafted = craftController.DoCraft(recipe.RecipeId);
            }
            else
            {
                crafted = inventory.TryCraft(recipe, out failReason);
            }
            int resultAfter = inventory.GetItemTotalCount(recipe.ResultItemId);
            if (!crafted || resultAfter - resultBefore != recipe.ResultCount)
                return "FAIL 成品差值错误 recipeId=" + recipe.RecipeId + " reason=" + failReason;

            foreach (KeyValuePair<int, int> material in recipe.Materials)
            {
                int after = inventory.GetItemTotalCount(material.Key);
                if (materialBefore[material.Key] - after != material.Value)
                    return "FAIL 材料扣除错误 itemId=" + material.Key;
            }
            return "PASS 配方" + recipe.RecipeId + "消耗本轮掉落并精确生成成品";
        }

        /// <summary> 克隆当前背包槽位，供无副作用容量模拟。 </summary>
        private static List<BagSlotInfo> CloneBagSlots()
        {
            var result = new List<BagSlotInfo>();
            IReadOnlyList<BagSlotInfo> slots = GameRoot.Instance.BagController.GetAllSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                result.Add(new BagSlotInfo
                {
                    SlotId = slots[i].SlotId,
                    ItemId = slots[i].ItemId,
                    ItemCount = slots[i].ItemCount
                });
            }
            return result;
        }

        /// <summary> 在槽位副本中添加物品，模拟真实堆叠和空槽占用。 </summary>
        private static bool TryAddToSlots(List<BagSlotInfo> slots, int itemId, int count)
        {
            ItemInfo item = new ItemDao().GetItemById(itemId);
            if (item == null || item.MaxStack <= 0 || count <= 0) return false;

            int remaining = count;
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                BagSlotInfo slot = slots[i];
                if (slot.ItemId != itemId || slot.ItemCount >= item.MaxStack) continue;
                int add = Math.Min(item.MaxStack - slot.ItemCount, remaining);
                slot.ItemCount += add;
                remaining -= add;
            }
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                BagSlotInfo slot = slots[i];
                if (slot.ItemId != 0 || slot.ItemCount != 0) continue;
                int add = Math.Min(item.MaxStack, remaining);
                slot.ItemId = itemId;
                slot.ItemCount = add;
                remaining -= add;
            }
            return remaining == 0;
        }

        /// <summary> 在槽位副本中移除指定数量物品。 </summary>
        private static bool TryRemoveFromSlots(List<BagSlotInfo> slots, int itemId, int count)
        {
            if (GetSlotTotal(slots, itemId) < count) return false;
            int remaining = count;
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                BagSlotInfo slot = slots[i];
                if (slot.ItemId != itemId || slot.ItemCount <= 0) continue;
                int remove = Math.Min(slot.ItemCount, remaining);
                slot.ItemCount -= remove;
                remaining -= remove;
                if (slot.ItemCount == 0) slot.ItemId = 0;
            }
            return remaining == 0;
        }

        /// <summary> 统计槽位副本中的指定物品数量。 </summary>
        private static int GetSlotTotal(List<BagSlotInfo> slots, int itemId)
        {
            int total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].ItemId == itemId) total += slots[i].ItemCount;
            }
            return total;
        }

        /// <summary> 读取拾取物私有序列化整数，仅用于编辑器验收。 </summary>
        private static int ReadPickupInt(PickupItem pickup, string propertyName)
        {
            if (pickup == null) return -1;
            SerializedProperty property = new SerializedObject(pickup).FindProperty(propertyName);
            return property != null ? property.intValue : -1;
        }

        /// <summary> 捕获全部背包槽位，供冒烟检查结束后原样恢复数据库和内存。 </summary>
        private static void CaptureBagSnapshot(IInventory inventory)
        {
            _bagSnapshot.Clear();
            if (inventory == null) return;
            IReadOnlyList<BagSlotInfo> slots = GameRoot.Instance.BagController.GetAllSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                BagSlotInfo slot = slots[i];
                _bagSnapshot.Add(new BagSlotInfo
                {
                    SlotId = slot.SlotId,
                    ItemId = slot.ItemId,
                    ItemCount = slot.ItemCount
                });
            }
            _bagSnapshotCaptured = true;
            SessionState.SetString(KeyBagSnapshot, SerializeBagSnapshot());
        }

        /// <summary> 将背包快照编码为跨域重载可保留的文本。 </summary>
        private static string SerializeBagSnapshot()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _bagSnapshot.Count; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append(_bagSnapshot[i].SlotId).Append(',')
                    .Append(_bagSnapshot[i].ItemId).Append(',')
                    .Append(_bagSnapshot[i].ItemCount);
            }
            return builder.ToString();
        }

        /// <summary> 域重载后从 SessionState 恢复库存快照。 </summary>
        private static void LoadBagSnapshotFromSession()
        {
            string serialized = SessionState.GetString(KeyBagSnapshot, string.Empty);
            if (string.IsNullOrEmpty(serialized)) return;

            _bagSnapshot.Clear();
            string[] slotTexts = serialized.Split(';');
            for (int i = 0; i < slotTexts.Length; i++)
            {
                string[] values = slotTexts[i].Split(',');
                if (values.Length != 3 || !int.TryParse(values[0], out int slotId)
                    || !int.TryParse(values[1], out int itemId)
                    || !int.TryParse(values[2], out int itemCount))
                {
                    _bagSnapshot.Clear();
                    _bagSnapshotCorrupted = true;
                    _bagSnapshotRecoveredAfterReload = true;
                    return;
                }
                _bagSnapshot.Add(new BagSlotInfo
                {
                    SlotId = slotId,
                    ItemId = itemId,
                    ItemCount = itemCount
                });
            }
            _bagSnapshotCaptured = _bagSnapshot.Count > 0;
            _bagSnapshotRecoveredAfterReload = _bagSnapshotCaptured;
        }

        /// <summary> 事务恢复冒烟检查前的背包槽位，并同步实时内存。 </summary>
        private static void RestoreBagSnapshot()
        {
            if (!_bagSnapshotCaptured || SqliteManager.Instance == null || !SqliteManager.Instance.IsReady)
                return;

            try
            {
                var bagDao = new BagDao();
                SqliteManager.Instance.RunInTransaction(() =>
                {
                    for (int i = 0; i < _bagSnapshot.Count; i++)
                        bagDao.UpdateSlot(_bagSnapshot[i]);
                });

                _bagSnapshotRestored = true;
                SessionState.EraseString(KeyBagSnapshot);

                IReadOnlyList<BagSlotInfo> liveSlots = GameRoot.Instance?.BagController?.GetAllSlots();
                if (liveSlots == null || liveSlots.Count != _bagSnapshot.Count) return;
                for (int i = 0; i < liveSlots.Count; i++)
                {
                    liveSlots[i].SlotId = _bagSnapshot[i].SlotId;
                    liveSlots[i].ItemId = _bagSnapshot[i].ItemId;
                    liveSlots[i].ItemCount = _bagSnapshot[i].ItemCount;
                }
                EventBus.Emit(EventName.Bag_Changed);
            }
            catch (Exception exception)
            {
                _errors.Add("库存恢复异常: " + exception.Message);
            }
        }

        /// <summary> 将严格掉落检查标记为失败。 </summary>
        private static void FailLootCheck(string reason)
        {
            _lootResult = "FAIL " + reason;
            _craftResult = "FAIL 未完成严格制造链";
        }

        private static void SetPhase(int phase, double now)
        {
            _phase = phase;
            _phaseStart = (float)now;
            SessionState.SetInt(KeyPhase, phase);
            SessionState.SetFloat(KeyPhaseStart, (float)now);
        }

        private static void CollectActors()
        {
            _samples.Clear();

            EnemyBrain[] brains = UnityEngine.Object.FindObjectsOfType<EnemyBrain>();
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i] == null) continue;
                EnemySample sample = new EnemySample
                {
                    Brain = brains[i],
                    LastPosition = brains[i].transform.position,
                    Root = brains[i].GetComponentInParent<CharacterRoot>()
                };
                _samples.Add(sample);

                // 攻击起手：即便剪辑上没有 AttackHit 事件，兜底秒数到点也应自动结算一次
                if (sample.Root != null && sample.Root.Equipment != null)
                    sample.Root.Equipment.AttackCommitted += weapon => HandleEnemyAttackCommitted(sample);
            }

            _player = null;
            CharacterRoot[] roots = UnityEngine.Object.FindObjectsOfType<CharacterRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].IsPlayerControlled)
                {
                    _player = roots[i];
                    break;
                }
            }

            if (_player != null && _player.Health != null)
                _player.Health.Damaged += HandlePlayerDamaged;
        }

        private static void HandleEnemyAttackCommitted(EnemySample sample)
        {
            _attackStarts++;
            if (_firstAttackTime < 0f) _firstAttackTime = (float)EditorApplication.timeSinceStartup;

            // 取消测试：目标一起手立刻击杀，之后迟到的动画事件/兜底都不许再补伤害
            if (_cancelStage == 1 && _cancelBaselineSet && sample == _cancelTarget)
            {
                Kill(sample);
                _cancelStage = 2;
                _cancelStageStart = (float)EditorApplication.timeSinceStartup;
            }
        }

        private static void HandlePlayerDamaged(float remainingHealth)
        {
            _damageEvents++;
            if (_firstDamageTime < 0f) _firstDamageTime = (float)EditorApplication.timeSinceStartup;
        }

        private static void Kill(EnemySample sample)
        {
            if (sample == null || sample.Root == null || sample.Root.Health == null) return;
            DamageContext context = new DamageContext
            {
                amount = CancelKillDamage,
                attacker = _player != null ? _player.gameObject : null,
                sourceFaction = Faction.Player,
                hitPoint = sample.Brain.transform.position
            };
            sample.KilledBySmokeCheck = true;
            sample.Root.Health.TakeDamage(context);
        }

        /// <summary>
        /// 把玩家挪到首个敌人附近的空地上，制造追击场景。
        /// 优先用敌人正前方，被挡住就按 45° 递增找第一个无遮挡方向，最后贴地放置。
        /// </summary>
        private static void SetupChase()
        {
            if (_player == null || _samples.Count == 0) return;

            // 早期版本把敌人瞬移到玩家身边——结果与「离出生点太远就归位」的防区约束冲突，
            // 敌人掉头就往家跑（实测结束距离反而从 4.7m 变成 15.8m）。
            // 所以改为：敌人一个都不动，只把玩家送到它们的质心，让敌人自己聚过来。
            Vector3 centroid = Vector3.zero;
            int n = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].Brain == null) continue;
                centroid += _samples[i].Brain.transform.position;
                n++;
            }
            if (n == 0) return;
            centroid /= n;

            Vector3 chosen = GroundPoint(centroid);
            _playerPlannedPosition = chosen;

            // 直接改 transform.position 会被 CharacterController 当成「穿墙瞬移」拉回上一个安全
            // 位置（实测落点偏差 11 米）——先禁用控制器再挪，挪完再启用，顺带重置它的内部状态。
            CharacterController controller = _player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            _player.transform.position = chosen;
            if (controller != null) controller.enabled = true;

            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample s = _samples[i];
                if (s.Brain == null) continue;
                s.LastPosition = s.Brain.transform.position;
                s.StartDistanceToPlayer = Vector3.Distance(s.Brain.transform.position, chosen);
                s.MinDistanceToPlayer = s.StartDistanceToPlayer;
            }

            // 任务进度靠事件驱动（拾取 / 制造 / 击杀），接晚了本轮就凑不满目标，
            // 所以要在击杀（取消测试）与掉落拾取之前先接取。
            AcceptQuestForTest();
        }

        /// <summary> 接取任务：优先走发布者交互，拿不到发布者时直接调控制器。 </summary>
        private static void AcceptQuestForTest()
        {
            QuestController controller =
                GameRoot.Instance != null ? GameRoot.Instance.QuestController : null;
            if (controller == null)
            {
                _questAcceptResult = "SKIP 拿不到任务控制器";
                return;
            }
            if (_player == null)
            {
                _questAcceptResult = "SKIP 没有玩家，无法交互接取";
                return;
            }

            QuestGiverInteractable giver =
                UnityEngine.Object.FindObjectOfType<QuestGiverInteractable>(true);
            if (giver != null)
            {
                giver.OnInteract(_player.gameObject);
                if (controller.State != QuestState.Active) controller.TryAccept();
            }
            else
            {
                controller.TryAccept();
            }

            _questAcceptResult = controller.State == QuestState.Active
                ? "PASS 接取成功，状态=" + controller.State
                  + (giver != null ? "（经发布者交互）" : "（直接调控制器）")
                : "FAIL 接取失败，状态=" + controller.State;
        }

        /// <summary> 给定水平位置，贴地返回（避免角色被塞进地面或悬空）。 </summary>
        private static Vector3 GroundPoint(Vector3 position)
        {
            RaycastHit ground;
            if (Physics.Raycast(position + Vector3.up * 8f, Vector3.down, out ground, 30f))
                return ground.point + Vector3.up * 0.1f;
            return position;
        }

        private static void SampleFrame()
        {
            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample s = _samples[i];
                if (s.Brain == null) continue;

                Vector3 p = s.Brain.transform.position;
                s.MovedDistance += Vector3.Distance(p, s.LastPosition);
                s.LastPosition = p;
                s.Frames++;

                LocalAvoidanceStatus status = s.Brain.AvoidanceStatus;
                if (status == LocalAvoidanceStatus.Blocked) s.BlockedFrames++;
                else if (status == LocalAvoidanceStatus.Adjusting) s.AdjustingFrames++;
                if (s.Brain.IsAvoidanceNeighborBufferSaturated) s.SaturatedFrames++;
                s.NeighborSum += s.Brain.AvoidanceNeighborCount;

                EnemyAIState state = s.Brain.CurrentState;
                if (state == EnemyAIState.Chase || state == EnemyAIState.Attack) s.SawChase = true;
                if (s.Brain.HasSharedAlert) s.SawSharedAlert = true;
                if (s.Brain.LastReceivedAlertSequence > s.LastAlertSequence)
                    s.LastAlertSequence = s.Brain.LastReceivedAlertSequence;

                if (s.Brain.NavigationStatus == EnemyNavigationStatus.Failed) s.NavFailedFrames++;
                if (s.Brain.NavigationFailure != EnemyNavigationFailure.None)
                    s.LastNavFailure = s.Brain.NavigationFailure;
                s.MoveDirSum += s.Brain.AdjustedNavigationDirection.magnitude;

                if (_player != null)
                {
                    float d = Vector3.Distance(p, _player.transform.position);
                    if (d < s.MinDistanceToPlayer) s.MinDistanceToPlayer = d;
                    s.EndDistanceToPlayer = d;
                }
            }

            for (int i = 0; i < _samples.Count; i++)
            {
                for (int j = i + 1; j < _samples.Count; j++)
                {
                    if (_samples[i].Brain == null || _samples[j].Brain == null) continue;
                    float d = Vector3.Distance(
                        _samples[i].Brain.transform.position,
                        _samples[j].Brain.transform.position);
                    if (d < _minPairDistance) _minPairDistance = d;
                }
            }
        }

        // ── 收尾与报告 ──────────────────────────────────────────────

        private static void Finish(string reason)
        {
            _phase = 99;
            SessionState.SetBool(KeyRunning, false);
            SessionState.SetInt(KeyPhase, 99);

            if (_bagSnapshotCaptured && !_bagSnapshotRestored)
                RestoreBagSnapshot();

            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            sb.AppendLine("=== PlayMode Smoke: "
                + (string.IsNullOrEmpty(_scenePath) ? "(未指定场景)" : _scenePath) + " ===");
            sb.AppendLine("[INFO] 采样节奏: 静置 " + SettleSeconds.ToString("F1")
                + "s + 基线 " + BaselineSeconds.ToString("F1")
                + "s + 追击 " + ChaseSeconds.ToString("F1") + "s");
            sb.AppendLine("[INFO] 敌人数量: " + _samples.Count + "，玩家: "
                + (_player != null ? _player.name : "(未找到)"));
            if (!string.IsNullOrEmpty(reason)) sb.AppendLine("[INFO] " + reason);

            // 1. 运行期异常
            if (_errors.Count == 0)
            {
                sb.AppendLine("[PASS] 运行期无 Error/Exception 日志");
                pass++;
            }
            else
            {
                sb.AppendLine("[FAIL] 运行期错误日志 " + _errors.Count + " 条，前 3 条：");
                for (int i = 0; i < _errors.Count && i < 3; i++) sb.AppendLine("       " + _errors[i]);
                fail++;
            }

            // 2. 基线位移：导航 + 输入链路活着
            float avgMoved = 0f;
            for (int i = 0; i < _samples.Count; i++) avgMoved += _samples[i].MovedDistance;
            if (_samples.Count > 0) avgMoved /= _samples.Count;

            bool movedOk = _samples.Count > 0 && avgMoved > MinBaselineMove;
            sb.AppendLine((movedOk ? "[PASS] " : "[FAIL] ")
                + "敌人总位移: 平均 " + avgMoved.ToString("F2") + " m（阈值 > "
                + MinBaselineMove.ToString("F2") + "）");
            if (movedOk) pass++; else fail++;

            // 3. 追击接近
            int approached = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample s = _samples[i];
                if (s.StartDistanceToPlayer <= 0f) continue;
                if (s.StartDistanceToPlayer - s.MinDistanceToPlayer > MinApproach) approached++;
            }
            bool chaseOk = _player != null && approached > 0;
            sb.AppendLine((chaseOk ? "[PASS] " : "[FAIL] ")
                + "追击接近: " + approached + "/" + _samples.Count
                + " 个敌人与玩家距离缩短 > " + MinApproach.ToString("F1") + " m");
            if (chaseOk) pass++; else fail++;

            // 4. 避障阻塞占比
            float maxBlocked = 0f;
            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample s = _samples[i];
                if (s.Frames <= 0) continue;
                float ratio = s.BlockedFrames / (float)s.Frames;
                if (ratio > maxBlocked) maxBlocked = ratio;
            }
            bool blockOk = maxBlocked <= MaxBlockedRatio;
            sb.AppendLine((blockOk ? "[PASS] " : "[FAIL] ")
                + "避障阻塞帧占比最高 " + (maxBlocked * 100f).ToString("F1") + "%（阈值 <= "
                + (MaxBlockedRatio * 100f).ToString("F0") + "%）");
            if (blockOk) pass++; else fail++;

            // 5. 敌人两两间距（只看不判：有人为贴脸攻击的场景）
            if (_minPairDistance < float.MaxValue)
            {
                sb.AppendLine("[INFO] 敌人两两最小间距: " + _minPairDistance.ToString("F2")
                    + " m（< " + MinPairDistance.ToString("F2") + " 说明挤成一团）");
            }

            // 6. 敌群报警（场景站位不保证有接收者，因此先记录传播事实，不强制判失败）
            int alertReceivers = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample sample = _samples[i];
                if (sample.SawSharedAlert || sample.LastAlertSequence > 0) alertReceivers++;
            }
            sb.AppendLine("[INFO] 敌群报警: 发布序列=" + EnemyRegistry.AlertSequence
                + "，最后一次投递=" + EnemyRegistry.LastAlertRecipientCount
                + "，本轮曾接收敌人=" + alertReceivers + "/" + _samples.Count);

            // 7. GC（只记录：编辑器自身也分配，绝对值不能当证据）
            long gcDeltaKb = (_gcAtChaseEnd - _gcAtChaseStart) / 1024L;
            sb.AppendLine("[INFO] 追击阶段托管堆变化: "
                + (gcDeltaKb >= 0 ? "+" : "") + gcDeltaKb + " KB / "
                + ChaseSeconds.ToString("F0") + "s");

            // 7. A* 无进展告警次数（巡逻期刷屏说明导航本身在原地打转）
            sb.AppendLine("[INFO] A* NoProgress 告警次数: " + _navFailWarnings);

            // 8. 玩家瞬移是否生效（差值大说明被 CharacterController / 角色逻辑拉回原位了）
            if (_player != null)
            {
                sb.AppendLine("[INFO] 玩家落点偏差: "
                    + Vector3.Distance(_player.transform.position, _playerPlannedPosition).ToString("F2")
                    + " m（大 = 瞬移没生效，玩家被拉回原位）");
            }

            // 每敌人明细
            for (int i = 0; i < _samples.Count; i++)
            {
                EnemySample s = _samples[i];
                if (s.Brain == null) continue;
                float blockedPct = s.Frames > 0 ? s.BlockedFrames * 100f / s.Frames : 0f;
                float avgNeighbors = s.Frames > 0 ? s.NeighborSum / (float)s.Frames : 0f;
                sb.AppendLine("[INFO]   " + s.Brain.name
                    + " | 状态=" + s.Brain.CurrentState
                    + " | 影响图=" + (s.Brain.IsInfluenceMapReady ? "Ready" : "Missing")
                    + " | 当前影响=" + s.Brain.CurrentInfluenceCost
                    + " | 报警序列=" + s.LastAlertSequence
                    + " | 见过报警=" + s.SawSharedAlert
                    + " | 见过Chase=" + s.SawChase
                    + " | 位移=" + s.MovedDistance.ToString("F2") + "m"
                    + " | 起始距玩家=" + s.StartDistanceToPlayer.ToString("F2") + "m"
                    + " | 最近=" + s.MinDistanceToPlayer.ToString("F2") + "m"
                    + " | 结束距玩家=" + s.EndDistanceToPlayer.ToString("F2") + "m"
                    + " | 阻塞=" + blockedPct.ToString("F1") + "%"
                    + " | 平均邻居=" + avgNeighbors.ToString("F2")
                    + " | 导航失败帧=" + s.NavFailedFrames + "/" + s.Frames
                    + " | 失败原因=" + s.LastNavFailure
                    + " | 平均方向模长=" + (s.Frames > 0 ? s.MoveDirSum / s.Frames : 0f).ToString("F2")
                    + " | 缓冲饱和帧=" + s.SaturatedFrames);
            }

            // 9. 攻击前摇与结算（09-18 批次：无动画事件时全部依赖 attackCommitFallbackSeconds 兜底）
            if (_attackStarts > 0)
            {
                bool damaged = _damageEvents > 0;
                sb.AppendLine((damaged ? "[PASS] " : "[FAIL] ")
                    + "攻击结算: 敌人起手 " + _attackStarts + " 次 → 玩家受伤 " + _damageEvents
                    + " 次（无 AttackHit 事件时应由兜底结算）");
                if (damaged) pass++; else fail++;

                bool once = _damageEvents <= _attackStarts;
                sb.AppendLine((once ? "[PASS] " : "[FAIL] ")
                    + "每个动作最多结算一次: 受伤 " + _damageEvents + " <= 起手 " + _attackStarts);
                if (once) pass++; else fail++;

                float delay = _firstDamageTime >= 0f && _firstAttackTime >= 0f
                    ? _firstDamageTime - _firstAttackTime : -1f;
                bool windup = delay >= MinWindupSeconds;
                sb.AppendLine((windup ? "[PASS] " : "[FAIL] ")
                    + "攻击前摇: 首次起手→首次结算 " + delay.ToString("F3") + " s（阈值 >= "
                    + MinWindupSeconds.ToString("F2") + "；兜底配置越接近命中帧手感越好）");
                if (windup) pass++; else fail++;
            }
            else
            {
                sb.AppendLine("[INFO] 攻击观察: 本轮敌人未发起攻击，攻击相关判定跳过");
            }

            if (!string.IsNullOrEmpty(_cancelResult))
            {
                bool cancelOk = _cancelResult.StartsWith("PASS");
                if (cancelOk) pass++;
                else if (!_cancelResult.StartsWith("SKIP")) fail++;
                sb.AppendLine((cancelOk ? "[PASS] " : "[INFO] ") + "前摇取消: " + _cancelResult);
            }

            // 10. 严格死亡掉落与拾取：必须来自本轮 Loot_ItemSpawned，并精确入包一次。
            bool lootOk = !string.IsNullOrEmpty(_lootResult) && _lootResult.StartsWith("PASS");
            sb.AppendLine((lootOk ? "[PASS] " : "[FAIL] ") + "死亡掉落与拾取: "
                + (_lootResult ?? "FAIL 未执行"));
            if (lootOk) pass++; else fail++;

            // 11. 严格制造链：配方必须消耗本轮掉落，且全部库存差值精确。
            bool craftOk = !string.IsNullOrEmpty(_craftResult) && _craftResult.StartsWith("PASS");
            sb.AppendLine((craftOk ? "[PASS] " : "[FAIL] ") + "制造链: "
                + (_craftResult ?? "FAIL 未执行"));

            // 12. 任务链路：接取 → 目标事件（击杀/拾取/制造）→ 交付领奖
            if (!string.IsNullOrEmpty(_questAcceptResult))
            {
                bool acceptOk = _questAcceptResult.StartsWith("PASS", StringComparison.Ordinal);
                if (acceptOk) pass++;
                else if (!_questAcceptResult.StartsWith("SKIP", StringComparison.Ordinal)) fail++;
                sb.AppendLine((acceptOk ? "[PASS] " : "[INFO] ") + "任务接取: " + _questAcceptResult);
            }
            if (!string.IsNullOrEmpty(_questTurnInResult))
            {
                bool turnInOk = _questTurnInResult.StartsWith("PASS", StringComparison.Ordinal);
                if (turnInOk) pass++;
                else if (!_questTurnInResult.StartsWith("INFO", StringComparison.Ordinal)
                         && !_questTurnInResult.StartsWith("SKIP", StringComparison.Ordinal)) fail++;
                sb.AppendLine((turnInOk ? "[PASS] " : "[INFO] ") + "任务交付: " + _questTurnInResult);
            }
            if (craftOk) pass++; else fail++;

            bool restored = !_bagSnapshotCorrupted && (!_bagSnapshotCaptured || _bagSnapshotRestored);
            sb.AppendLine((restored ? "[PASS] " : "[FAIL] ") + "库存恢复: "
                + (restored ? "冒烟检查前状态已恢复" : "恢复失败，可能污染测试存档"));
            if (restored) pass++; else fail++;

            sb.AppendLine();
            sb.AppendLine("TOTAL: pass=" + pass + " fail=" + fail);
            sb.AppendLine(fail == 0 ? ResultMarkerPass : ResultMarkerFail);

            bool reportWritten = true;
            try
            {
                File.WriteAllText(_resultPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                reportWritten = false;
                fail++;
                Debug.LogError("[PlayModeSmokeCheck] 结果文件写入失败: " + exception.Message);
            }

            if (_subscribed)
            {
                Application.logMessageReceived -= HandleLog;
                EditorApplication.update -= Tick;
                _subscribed = false;
            }
            _lootDroppedToken?.Dispose();
            _lootSpawnToken?.Dispose();
            _pickupCompletedToken?.Dispose();
            _lootDroppedToken = null;
            _lootSpawnToken = null;
            _pickupCompletedToken = null;

            EditorApplication.Exit(fail == 0 && reportWritten ? 0 : 1);
        }
    }
}
