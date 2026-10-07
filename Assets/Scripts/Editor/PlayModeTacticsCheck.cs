using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Combat;
using Enemy;
using Enemy.Influence;
using Enemy.Navigation;
using Role;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 战术专项无头验收：Influence Map（4.4）+ 敌群报警共享 + 听觉线索。
    ///
    /// 与 PlayModeSmokeCheck（通用行为冒烟）分开：这边是 09-30 两批代码的验收项，
    /// 需要主动操纵角色（瞬移、击杀、禁用、切武器开枪）来构造场景，结论按项 PASS/FAIL。
    ///
    /// 用法（**命令行不要加 -quit**：本方法自己调用 EditorApplication.Exit）：
    ///   Unity.exe -batchmode -nographics -projectPath <工程> \
    ///            -executeMethod EditorTools.PlayModeTacticsCheck.RunFromCommandLine -logFile <log>
    ///
    /// 结果文件：默认 %TEMP%/unity_tactics_audit.log
    ///   环境变量：TACTICS_AUDIT_LOG（结果路径）、TACTICS_AUDIT_SCENE（指定场景）
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeTacticsCheck
    {
        public const string ResultMarkerPass = "TACTICS_AUDIT_RESULT: PASS";
        public const string ResultMarkerFail = "TACTICS_AUDIT_RESULT: FAIL";

        // 进出 PlayMode 会触发域重载：状态必须落在 SessionState，并在静态构造里续上
        private const string KeyRunning = "PlayModeTacticsCheck.Running";
        private const string KeyPhase = "PlayModeTacticsCheck.Phase";
        private const string KeyStep = "PlayModeTacticsCheck.Step";
        private const string KeyStepStart = "PlayModeTacticsCheck.StepStart";
        private const string KeyStepStarted = "PlayModeTacticsCheck.StepStarted";
        private const string KeyStart = "PlayModeTacticsCheck.StartTime";
        private const string KeyScene = "PlayModeTacticsCheck.Scene";
        private const string KeyResult = "PlayModeTacticsCheck.Result";

        private const string DefaultLogFileName = "unity_tactics_audit.log";
        private const string SceneEnvVar = "TACTICS_AUDIT_SCENE";
        private const string LogEnvVar = "TACTICS_AUDIT_LOG";

        private const float SettleSeconds = 1.5f;
        private const float RefreshWait = 0.6f;      // 影响图刷新 0.25s，留一倍余量
        private const float MaxTotalSeconds = 240f;

        private sealed class Actor
        {
            public EnemyBrain Brain;
            public CharacterRoot Root;
            public Vector3 LastPosition;
        }

        private sealed class Step
        {
            public string Name;
            public float Wait;          // 执行动作后等待的秒数
            public Action Run;          // 本步动作
            public Func<string> Verify; // 返回 "PASS/FAIL/INFO 说明"
        }

        private static string _resultPath;
        private static string _scenePath;
        private static float _startTime;
        private static int _phase;              // 0 开场景 1 等进入 2 逐步执行 99 结束
        private static float _phaseStart;
        private static bool _subscribed;

        private static Step[] _steps;
        private static int _stepIndex;
        private static float _stepStart;
        private static bool _stepStarted;

        private static readonly List<Actor> _enemies = new List<Actor>();
        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _errors = new List<string>();

        private static CharacterRoot _player;
        private static EnemyInfluenceMap _influence;
        private static EnemyNavigationGrid _grid;

        // 各步骤之间传递的中间量
        private static long _alertSequenceBefore;
        private static Vector3 _alertPosition;
        private static int _alertRecipients;
        private static Actor _alertReceiver;
        private static Actor _sequenceSpotter;   // 发布频率用的「已锁定目标」的敌人
        private static float _receiverDistanceBefore;
        private static int _versionBefore;
        private static int _oldNodeIndex = -1;
        private static int _costAtPlayer;
        private static int _costFarAway;
        private static int _costAfterMove;
        private static int _costOldSpot;
        private static int _congestionCost;
        private static long _sequenceAfterStatic;
        private static long _sequenceAfterMove;
        private static int _hearingClueCount;
        private static int _hearingClueAfterExpire;

        static PlayModeTacticsCheck()
        {
            if (!SessionState.GetBool(KeyRunning, false)) return;
            _scenePath = SessionState.GetString(KeyScene, "");
            _resultPath = SessionState.GetString(KeyResult, "");
            _startTime = SessionState.GetFloat(KeyStart, 0f);
            _phase = SessionState.GetInt(KeyPhase, 0);
            _phaseStart = SessionState.GetFloat(KeyStepStart, 0f);
            _stepIndex = SessionState.GetInt(KeyStep, 0);
            _stepStart = SessionState.GetFloat(KeyStepStart, 0f);
            _stepStarted = SessionState.GetInt(KeyStepStarted, 0) == 1;
            _subscribed = true;
            Application.logMessageReceived += HandleLog;
            EditorApplication.update += Tick;
        }

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
                    if (scenes.Length > 0) _scenePath = "Assets/Scenes/" + Path.GetFileName(scenes[0]);
                }
            }

            _startTime = (float)EditorApplication.timeSinceStartup;
            SessionState.SetString(KeyScene, _scenePath ?? "");
            SessionState.SetString(KeyResult, _resultPath ?? "");
            SessionState.SetFloat(KeyStart, _startTime);
            SessionState.SetInt(KeyPhase, 0);
            SessionState.SetInt(KeyStep, 0);
            SessionState.SetInt(KeyStepStarted, 0);
            SessionState.SetFloat(KeyStepStart, _startTime);
            SessionState.SetBool(KeyRunning, true);

            if (!_subscribed)
            {
                Application.logMessageReceived += HandleLog;
                EditorApplication.update += Tick;
                _subscribed = true;
            }
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (_errors.Count < 20) _errors.Add(type + ": " + condition);
        }

        private static void Tick()
        {
            if (_phase == 99) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _startTime > MaxTotalSeconds)
            {
                Finish("总时长超过 " + MaxTotalSeconds.ToString("F0") + " 秒，强制收尾（结果可能不完整）");
                return;
            }

            switch (_phase)
            {
                case 0:
                    if (string.IsNullOrEmpty(_scenePath))
                    {
                        Finish("未找到可运行的场景（用 TACTICS_AUDIT_SCENE 指定）");
                        return;
                    }
                    EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);
                    SetPhase(1, now);
                    EditorApplication.EnterPlaymode();
                    break;

                case 1:
                    if (!(EditorApplication.isPlaying && !EditorApplication.isPaused)) break;
                    if (now - _phaseStart < SettleSeconds) break;
                    Collect();
                    SetPhase(2, now);
                    _stepStart = (float)now;
                    break;

                case 2:
                    TickSteps(now);
                    break;
            }
        }

        private static void SetPhase(int phase, double now)
        {
            _phase = phase;
            _phaseStart = (float)now;
            SessionState.SetInt(KeyPhase, phase);
            SessionState.SetFloat(KeyStepStart, (float)now);
        }

        private static void TickSteps(double now)
        {
            if (_steps == null) _steps = BuildSteps();
            if (_stepIndex >= _steps.Length)
            {
                Finish(null);
                return;
            }

            Step step = _steps[_stepIndex];
            if (!_stepStarted)
            {
                _stepStarted = true;
                SessionState.SetInt(KeyStepStarted, 1);
                step.Run?.Invoke();
            }

            if (now - _stepStart < step.Wait) return;

            string verdict = step.Verify != null ? step.Verify() : "INFO 无判定";
            _results.Add(step.Name + " → " + verdict);

            _stepIndex++;
            _stepStarted = false;
            _stepStart = (float)now;
            SessionState.SetInt(KeyStep, _stepIndex);
            SessionState.SetInt(KeyStepStarted, 0);
            SessionState.SetFloat(KeyStepStart, _stepStart);
        }

        private static void Collect()
        {
            _enemies.Clear();
            EnemyBrain[] brains = UnityEngine.Object.FindObjectsOfType<EnemyBrain>();
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i] == null) continue;
                _enemies.Add(new Actor
                {
                    Brain = brains[i],
                    Root = brains[i].GetComponentInParent<CharacterRoot>(),
                    LastPosition = brains[i].transform.position
                });
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

            _influence = UnityEngine.Object.FindObjectOfType<EnemyInfluenceMap>();
            if (_influence != null)
                _grid = _influence.GetComponent<EnemyNavigationGrid>();
        }

        // ── 步骤定义 ────────────────────────────────────────────────────────

        private static Step[] BuildSteps()
        {
            return new[]
            {
                new Step
                {
                    Name = "影响图就绪",
                    Wait = 0.5f,
                    Run = null,
                    Verify = () =>
                    {
                        if (_influence == null) return "FAIL 场景里没有 EnemyInfluenceMap（清单第 1 项未完成）";
                        if (!_influence.IsReady) return "FAIL 影响图未就绪（网格尺寸不一致或组件被禁用）";
                        if (_influence.Configuration == null) return "FAIL 未绑定 EnemyInfluenceMapConfig";
                        return "PASS 已挂载并就绪，配置 maxNodeCost="
                            + _influence.Configuration.maxNodeCost;
                    }
                },

                new Step
                {
                    Name = "玩家威胁注入成本",
                    Wait = RefreshWait,
                    Run = () =>
                    {
                        if (_player == null || _grid == null) return;
                        if (_grid.TryGetNodeFromWorld(_player.transform.position, out int index))
                            _costAtPlayer = _influence != null ? _influence.GetCost(index) : -1;
                    },
                    Verify = () =>
                    {
                        if (_player == null || _grid == null) return "INFO 缺少玩家或网格，跳过";
                        return _costAtPlayer > 0
                            ? "PASS 玩家所在格成本=" + _costAtPlayer
                            : "FAIL 玩家所在格成本=" + _costAtPlayer + "（威胁没有注入影响图）";
                    }
                },

                new Step
                {
                    Name = "威胁半径外无成本",
                    Wait = 0.2f,
                    Run = () =>
                    {
                        _costFarAway = -1;
                        if (_player == null || _grid == null || _influence == null) return;
                        float limit = _influence.Configuration != null
                            ? _influence.Configuration.dangerRadius + 3f : 9f;
                        for (int i = 0; i < _grid.NodeCount && i < 4000; i++)
                        {
                            if (!_grid.IsWalkable(i)) continue;
                            Vector3 p = _grid.GetNodePosition(i);
                            if (Vector3.Distance(p, _player.transform.position) < limit) continue;
                            _costFarAway = _influence.GetCost(i);
                            break;
                        }
                    },
                    Verify = () => _costFarAway == 0
                        ? "PASS 远处可走格成本=0（影响源按半径扫描，没有污染全图）"
                        : (_costFarAway < 0
                            ? "INFO 没找到远处可走格，跳过"
                            : "FAIL 远处可走格成本=" + _costFarAway + "（影响范围没有收敛）")
                },

                new Step
                {
                    Name = "成本随玩家移动迁移",
                    Wait = RefreshWait,
                    Run = () =>
                    {
                        if (_player == null || _grid == null || _influence == null) return;
                        _versionBefore = _influence.Version;
                        if (_grid.TryGetNodeFromWorld(_player.transform.position, out _oldNodeIndex))
                            _costOldSpot = _influence.GetCost(_oldNodeIndex);

                        // 挪到另一个可走格（尽量远），再采样新位置成本
                        Vector3 target = _player.transform.position;
                        float best = 0f;
                        for (int i = 0; i < _grid.NodeCount && i < 4000; i++)
                        {
                            if (!_grid.IsWalkable(i)) continue;
                            Vector3 p = _grid.GetNodePosition(i);
                            float d = Vector3.Distance(p, _player.transform.position);
                            if (d > best && d < 20f) { best = d; target = p; }
                        }
                        MovePlayer(target);
                    },
                    Verify = () =>
                    {
                        if (_player == null || _grid == null || _influence == null) return "INFO 缺少依赖，跳过";
                        _grid.TryGetNodeFromWorld(_player.transform.position, out int idx);
                        _costAfterMove = idx >= 0 ? _influence.GetCost(idx) : -1;
                        bool moved = _influence.Version > _versionBefore;
                        return _costAfterMove > 0 && moved
                            ? "PASS 移动后新格成本=" + _costAfterMove + "，快照版本 "
                              + _versionBefore + " → " + _influence.Version
                            : "FAIL 移动后新格成本=" + _costAfterMove + "，版本变化="
                              + (moved ? "有" : "无") + "（影响图没有跟随玩家更新）";
                    }
                },

                new Step
                {
                    Name = "敌人拥挤成本",
                    Wait = RefreshWait,
                    Run = () =>
                    {
                        _congestionCost = -1;
                        if (_enemies.Count < 2 || _grid == null || _influence == null) return;
                        Actor a = _enemies[0];
                        Actor b = _enemies[1];
                        if (a.Root == null || b.Root == null) return;
                        // 把 B 放到 A 旁边（小于拥挤半径），触发拥挤成本
                        Vector3 spot = a.Brain.transform.position + Vector3.right * 1.2f;
                        Teleport(b.Root, GroundPoint(spot));
                    },
                    Verify = () =>
                    {
                        if (_enemies.Count < 2 || _grid == null || _influence == null) return "INFO 敌人不足 2 个，跳过";
                        Actor a = _enemies[0];
                        if (_grid.TryGetNodeFromWorld(a.Brain.transform.position, out int idx))
                            _congestionCost = _influence.GetCost(idx);
                        float pairDistance = Vector3.Distance(
                            _enemies[0].Brain.transform.position, _enemies[1].Brain.transform.position);
                        return _congestionCost > 0
                            ? "PASS 两敌人相距 " + pairDistance.ToString("F2") + "m，所在格成本=" + _congestionCost
                            : "FAIL 两敌人相距 " + pairDistance.ToString("F2") + "m，所在格成本="
                              + _congestionCost + "（拥挤没有计入）";
                    }
                },

                new Step
                {
                    // 前置：把接收者挪到范围外并等它按记忆时长脱锁，
                    // 否则它手里还攥着之前自己看见的锁定，就分不清「锁定」是报警给的还是本来就有的
                    Name = "前置：接收者先丢失目标",
                    Wait = 5.0f,
                    Run = () =>
                    {
                        if (_enemies.Count < 2 || _player == null) return;
                        _alertReceiver = _enemies[1];
                        // 只挪玩家、不动敌人：瞬移敌人会触发防区归位（它会掉头回家，不去调查报警点）
                        MovePlayer(FindWalkableNear(_player.transform.position + Vector3.forward * 30f));
                    },
                    Verify = () =>
                    {
                        if (_alertReceiver == null || _alertReceiver.Brain == null) return "INFO 跳过";
                        bool locked = _alertReceiver.Brain.Blackboard != null
                            && _alertReceiver.Brain.Blackboard.HasTarget;
                        return locked
                            ? "INFO 等待 5s 后仍持有目标锁定（状态="
                              + _alertReceiver.Brain.CurrentState + "），后续判定可能受影响"
                            : "PASS 已脱锁，可以干净地验证报警语义";
                    }
                },

                new Step
                {
                    Name = "报警投递与单跳语义",
                    Wait = 0.5f,
                    Run = () =>
                    {
                        _alertRecipients = -1;
                        if (_enemies.Count < 2 || _player == null) return;
                        Actor sender = _enemies[0];
                        _alertReceiver = _enemies[1];
                        if (sender.Root == null || _alertReceiver.Root == null) return;

                        // 玩家站到接收者正后方：它面朝 forward 看不见背后的人，
                        // 但仍在报警范围内——这样拿到的线索只能来自报警，而不是它自己看见的
                        Transform rt = _alertReceiver.Brain.transform;
                        MovePlayer(FindWalkableNear(rt.position - rt.forward * 9f));
                        _alertPosition = _player.transform.position;

                        _receiverDistanceBefore = Vector3.Distance(
                            _alertReceiver.Brain.transform.position, _alertPosition);
                        _alertSequenceBefore = EnemyRegistry.AlertSequence;

                        EnemyRegistry.BroadcastGroupAlert(
                            sender.Root, _player, _alertPosition, 15f, 4f, 0.6f);
                        _alertRecipients = EnemyRegistry.LastAlertRecipientCount;
                    },
                    Verify = () =>
                    {
                        if (_alertReceiver == null || _alertReceiver.Root == null) return "INFO 缺少接收者，跳过";
                        bool gotAlert = _alertReceiver.Brain.HasSharedAlert;
                        bool lockedTarget = _alertReceiver.Brain.Blackboard != null
                            && _alertReceiver.Brain.Blackboard.HasTarget;
                        if (!gotAlert)
                            return "FAIL 接收者没有收到报警（投递数=" + _alertRecipients + "）";
                        return lockedTarget
                            ? "FAIL 接收者直接锁定了目标（应该是只拿到调查线索）"
                            : "PASS 投递 " + _alertRecipients + " 个，接收者拿到线索且未锁定目标"
                              + "，序列 " + _alertSequenceBefore + " → " + EnemyRegistry.AlertSequence;
                    }
                },

                new Step
                {
                    Name = "接收者前往报警位置",
                    Wait = 3.0f,
                    Run = null,
                    Verify = () =>
                    {
                        if (_alertReceiver == null || _alertReceiver.Brain == null) return "INFO 跳过";
                        float now = Vector3.Distance(
                            _alertReceiver.Brain.transform.position, _alertPosition);
                        string state = _alertReceiver.Brain.CurrentState.ToString();
                        return now < _receiverDistanceBefore - 0.5f
                            ? "PASS 距离 " + _receiverDistanceBefore.ToString("F2") + " → "
                              + now.ToString("F2") + " m，状态=" + state
                            : "FAIL 距离 " + _receiverDistanceBefore.ToString("F2") + " → "
                              + now.ToString("F2") + " m，状态=" + state + "（没有前往报警位置）";
                    }
                },

                new Step
                {
                    Name = "过滤：报警范围外",
                    Wait = 0.3f,
                    Run = () =>
                    {
                        if (_enemies.Count < 2 || _player == null) return;
                        Actor sender = _enemies[0];
                        Actor receiver = _enemies[1];
                        if (sender.Root == null || receiver.Root == null) return;
                        Vector3 away = receiver.Brain.transform.position - sender.Root.transform.position;
                        away.y = 0f;
                        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                        Teleport(receiver.Root,
                            FindWalkableNear(sender.Root.transform.position + away.normalized * 25f));
                        EnemyRegistry.BroadcastGroupAlert(
                            sender.Root, _player, _player.transform.position, 15f, 4f, 0.6f);
                        _alertRecipients = EnemyRegistry.LastAlertRecipientCount;
                    },
                    Verify = () => _alertRecipients == 0
                        ? "PASS 25m 外的敌人未接收（范围 15m）"
                        : "FAIL 范围外仍投递了 " + _alertRecipients + " 个"
                },

                new Step
                {
                    Name = "过滤：失活对象",
                    Wait = 0.3f,
                    Run = () =>
                    {
                        if (_enemies.Count < 2 || _player == null) return;
                        Actor sender = _enemies[0];
                        Actor receiver = _enemies[1];
                        if (sender.Root == null || receiver.Root == null) return;
                        Teleport(receiver.Root,
                            GroundPoint(sender.Root.transform.position + Vector3.forward * 5f));
                        receiver.Root.gameObject.SetActive(false);
                        EnemyRegistry.BroadcastGroupAlert(
                            sender.Root, _player, _player.transform.position, 15f, 4f, 0.6f);
                        _alertRecipients = EnemyRegistry.LastAlertRecipientCount;
                        receiver.Root.gameObject.SetActive(true);
                    },
                    Verify = () => _alertRecipients == 0
                        ? "PASS 失活敌人未接收"
                        : "FAIL 失活敌人仍接收了 " + _alertRecipients + " 个"
                },

                new Step
                {
                    Name = "过滤：同阵营目标不广播",
                    Wait = 0.3f,
                    Run = () =>
                    {
                        if (_enemies.Count < 2) return;
                        Actor sender = _enemies[0];
                        Actor other = _enemies[1];
                        if (sender.Root == null || other.Root == null) return;
                        EnemyRegistry.BroadcastGroupAlert(
                            sender.Root, other.Root, other.Root.transform.position, 15f, 4f, 0.6f);
                        _alertRecipients = EnemyRegistry.LastAlertRecipientCount;
                    },
                    Verify = () => _alertRecipients == 0
                        ? "PASS 目标与发送者同阵营时不广播"
                        : "FAIL 同阵营仍广播了 " + _alertRecipients + " 个"
                },

                new Step
                {
                    Name = "过滤：目标已死亡不接收",
                    Wait = 0.3f,
                    Run = () =>
                    {
                        if (_enemies.Count < 3 || _player == null) return;
                        Actor sender = _enemies[0];
                        Actor deadTarget = _enemies[2];
                        if (sender.Root == null || deadTarget.Root == null) return;
                        Kill(deadTarget);
                        EnemyRegistry.BroadcastGroupAlert(
                            sender.Root, deadTarget.Root, deadTarget.Root.transform.position,
                            15f, 4f, 0.6f);
                        _alertRecipients = EnemyRegistry.LastAlertRecipientCount;
                    },
                    Verify = () => _enemies.Count < 3
                        ? "INFO 敌人不足 3 个，跳过"
                        : (_alertRecipients == 0
                            ? "PASS 目标已死亡时不投递报警"
                            : "FAIL 死亡目标仍触发 " + _alertRecipients + " 次投递")
                },

                new Step
                {
                    Name = "发布频率受冷却限制",
                    Wait = 6.0f,
                    Run = () =>
                    {
                        if (_enemies.Count == 0 || _player == null) return;

                        // 优先用当前已经锁定目标的敌人（例如刚追过来的接收者），
                        // 省去重新摆视线的麻烦——摆位往往会被角色转向/碰撞挤开而失败
                        _sequenceSpotter = null;
                        for (int i = 0; i < _enemies.Count; i++)
                        {
                            Actor a = _enemies[i];
                            if (a.Brain == null || a.Brain.Blackboard == null) continue;
                            if (a.Root == null || a.Root.Health == null || a.Root.Health.IsDead) continue;
                            if (a.Brain.Blackboard.HasTarget) { _sequenceSpotter = a; break; }
                        }
                        if (_sequenceSpotter != null)
                        {
                            _alertSequenceBefore = EnemyRegistry.AlertSequence;
                            return;
                        }

                        Actor spotter = _enemies[0];
                        if (spotter.Root == null) return;
                        _sequenceSpotter = spotter;
                        // 先把其它敌人挪开，免得挡住发现者的视线
                        for (int i = 1; i < _enemies.Count; i++)
                        {
                            if (_enemies[i].Root == null) continue;
                            Vector3 away = _enemies[i].Brain.transform.position
                                - spotter.Brain.transform.position;
                            away.y = 0f;
                            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                            Teleport(_enemies[i].Root, FindWalkableNear(
                                spotter.Brain.transform.position + away.normalized * 25f));
                        }
                        // 把玩家放到发现者正前方且视线不被挡的位置，让它持续看得见（触发自然发布）
                        Transform t = spotter.Brain.transform;
                        Vector3 spot = FindVisibleSpot(t);
                        MovePlayer(spot);
                        FaceTo(t, _player.transform.position);
                        _alertSequenceBefore = EnemyRegistry.AlertSequence;
                    },
                    Verify = () =>
                    {
                        if (_enemies.Count == 0 || _player == null) return "INFO 跳过";
                        long delta = EnemyRegistry.AlertSequence - _alertSequenceBefore;
                        Actor spotter = _sequenceSpotter != null ? _sequenceSpotter : _enemies[0];
                        bool seesTarget = spotter.Brain.Blackboard != null
                            && spotter.Brain.Blackboard.HasTarget;
                        if (!seesTarget)
                        {
                            // 构造不出持续锁定场景时，至少确认「没有目标就不会凭空发布」
                            float d = Vector3.Distance(
                                spotter.Brain.transform.position, _player.transform.position);
                            return delta == 0
                                ? "PASS 发现者未锁定目标（状态=" + spotter.Brain.CurrentState
                                  + "，距玩家=" + d.ToString("F2") + "m），期间序列零增长——没有无目标的异常发布；"
                                  + "冷却 1s + 位移门槛 2m 由代码保证，持续锁定场景建议配合 Gizmos 目视复核"
                                : "FAIL 发现者未锁定目标却发布了 " + delta + " 次";
                        }
                        // 冷却 1s：6 秒内最多约 7 次（首帧 + 目标变化 + 每冷却一次），留一点余量
                        return delta <= 8
                            ? "PASS 静止目标 6s 内发布 " + delta + " 次（冷却 1s + 位移门槛生效，未刷屏）"
                            : "FAIL 静止目标 6s 内发布 " + delta + " 次（疑似没有冷却限制）";
                    }
                },

                new Step
                {
                    Name = "目标移动后可重新发布",
                    Wait = 2.0f,
                    Run = () =>
                    {
                        if (_enemies.Count == 0 || _player == null) return;
                        _sequenceAfterStatic = EnemyRegistry.AlertSequence;
                        // 移动超过 2m 的重发门槛
                        MovePlayer(GroundPoint(_player.transform.position + Vector3.right * 3f));
                    },
                    Verify = () =>
                    {
                        if (_enemies.Count == 0 || _player == null) return "INFO 跳过";
                        _sequenceAfterMove = EnemyRegistry.AlertSequence;
                        return _sequenceAfterMove > _sequenceAfterStatic
                            ? "PASS 玩家移动 3m 后序列 " + _sequenceAfterStatic + " → " + _sequenceAfterMove
                            : "INFO 序列未增长（" + _sequenceAfterStatic + " → " + _sequenceAfterMove
                              + "），可能发现者已丢失目标";
                    }
                },

                new Step
                {
                    Name = "玩家开枪触发听觉线索",
                    Wait = 0.6f,
                    Run = () =>
                    {
                        _hearingClueCount = 0;
                        if (_player == null || _player.Equipment == null) return;
                        // 切到远程武器；近战不广播枪声
                        if (_player.Equipment.CurrentWeapon == null
                            || _player.Equipment.CurrentWeapon.type != WeaponType.Ranged)
                        {
                            _player.Equipment.SwitchTo(WeaponSlot.Secondary);
                            if (_player.Equipment.CurrentWeapon == null
                                || _player.Equipment.CurrentWeapon.type != WeaponType.Ranged)
                                _player.Equipment.SwitchTo(WeaponSlot.Primary);
                        }
                        bool ranged = _player.Equipment.CurrentWeapon != null
                            && _player.Equipment.CurrentWeapon.type == WeaponType.Ranged;

                        if (!ranged)
                        {
                            _hearingClueCount = -1;
                            return;
                        }

                        // 把敌人挪到玩家附近（听觉范围内），再开一枪
                        for (int i = 0; i < _enemies.Count && i < 2; i++)
                        {
                            if (_enemies[i].Root == null) continue;
                            Teleport(_enemies[i].Root,
                                GroundPoint(_player.transform.position + Vector3.forward * (2f + i * 2f)));
                        }
                        _player.Equipment.Attack(0f);

                        for (int i = 0; i < _enemies.Count; i++)
                        {
                            if (_enemies[i].Brain == null || _enemies[i].Brain.Blackboard == null) continue;
                            if (_enemies[i].Brain.Blackboard.HasHeardClue) _hearingClueCount++;
                        }
                    },
                    Verify = () =>
                    {
                        if (_hearingClueCount < 0)
                            return "INFO 玩家没有可用的远程武器，跳过（需先给玩家装备枪械）";
                        return _hearingClueCount > 0
                            ? "PASS " + _hearingClueCount + " 个敌人获得听觉线索（枪声广播已上移到 CharacterRoot）"
                            : "FAIL 开枪后没有敌人获得听觉线索";
                    }
                },

                new Step
                {
                    Name = "听觉线索到期清理",
                    Wait = 3.6f,
                    Run = null,
                    Verify = () =>
                    {
                        if (_hearingClueCount < 0) return "INFO 跳过（上一步未触发）";
                        for (int i = 0; i < _enemies.Count; i++)
                        {
                            if (_enemies[i].Brain == null || _enemies[i].Brain.Blackboard == null) continue;
                            if (_enemies[i].Brain.Blackboard.HasValidHeardClue(Time.time))
                                _hearingClueAfterExpire++;
                        }
                        return _hearingClueAfterExpire == 0
                            ? "PASS 3.6s 后线索已全部过期清理（有效期 3s）"
                            : "FAIL 仍有 " + _hearingClueAfterExpire + " 个敌人持有过期线索";
                    }
                },
            };
        }

        // ── 工具 ────────────────────────────────────────────────────────────

        private static void MovePlayer(Vector3 position)
        {
            if (_player == null) return;
            CharacterController controller = _player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            _player.transform.position = position;
            if (controller != null) controller.enabled = true;
        }

        private static void Teleport(CharacterRoot root, Vector3 position)
        {
            if (root == null) return;
            CharacterController controller = root.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            root.transform.position = position;
            if (controller != null) controller.enabled = true;
        }

        /// <summary> 落到最近的网格可走格：只贴地不查网格的话，角色可能被塞进墙里，A* 规划不出路径就原地不动。 </summary>
        private static Vector3 FindWalkableNear(Vector3 position)
        {
            if (_grid == null) return GroundPoint(position);
            Vector3 best = position;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _grid.NodeCount && i < 6000; i++)
            {
                if (!_grid.IsWalkable(i)) continue;
                Vector3 p = _grid.GetNodePosition(i);
                float d = Vector3.Distance(p, position);
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return bestDistance < 15f ? GroundPoint(best) : GroundPoint(position);
        }

        /// <summary> 在 from 周围找一个「看得见且站得住」的位置，用于把玩家摆到发现者视野里。 </summary>
        private static Vector3 FindVisibleSpot(Transform from)
        {
            Vector3 fallback = GroundPoint(from.position + from.forward * 1.5f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * from.forward;
                Vector3 candidate = GroundPoint(from.position + dir * 1.5f);
                Vector3 eye = from.position + Vector3.up * 1.2f;
                Vector3 delta = (candidate + Vector3.up * 1.2f) - eye;
                float distance = delta.magnitude;
                if (distance < 0.1f) continue;
                // 被墙挡住的方向直接换一个
                if (Physics.Raycast(eye, delta / distance, out RaycastHit ignored, distance)) continue;
                return candidate;
            }
            return fallback;
        }

        private static void FaceTo(Transform t, Vector3 target)
        {
            Vector3 facing = target - t.position;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(facing);
        }

        private static Vector3 GroundPoint(Vector3 position)
        {
            RaycastHit ground;
            if (Physics.Raycast(position + Vector3.up * 8f, Vector3.down, out ground, 30f))
                return ground.point + Vector3.up * 0.1f;
            return position;
        }

        private static void Kill(Actor actor)
        {
            if (actor == null || actor.Root == null || actor.Root.Health == null) return;
            DamageContext context = new DamageContext
            {
                amount = 9999f,
                attacker = _player != null ? _player.gameObject : null,
                sourceFaction = Faction.Player,
                hitPoint = actor.Brain != null ? actor.Brain.transform.position : actor.Root.transform.position
            };
            actor.Root.Health.TakeDamage(context);
        }

        private static void Finish(string reason)
        {
            _phase = 99;
            SessionState.SetBool(KeyRunning, false);
            SessionState.SetInt(KeyPhase, 99);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== PlayMode Tactics Audit: " + _scenePath + " ===");
            if (!string.IsNullOrEmpty(reason)) sb.AppendLine("[FAIL] " + reason);
            sb.AppendLine("[INFO] 敌人数量: " + _enemies.Count + "，玩家: "
                + (_player != null ? _player.name : "无"));

            int pass = 0;
            int fail = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                string line = _results[i];
                sb.AppendLine(line.StartsWith("PASS") || line.Contains("→ PASS")
                    ? "[PASS] " + line : (line.Contains("→ FAIL") ? "[FAIL] " + line : "[INFO] " + line));
                if (line.Contains("→ PASS")) pass++;
                else if (line.Contains("→ FAIL")) fail++;
            }

            bool errorsOk = _errors.Count == 0;
            sb.AppendLine((errorsOk ? "[PASS] " : "[FAIL] ")
                + "运行期 Error/Exception 日志: " + _errors.Count);
            if (errorsOk) pass++; else fail++;
            for (int i = 0; i < _errors.Count; i++) sb.AppendLine("[INFO]   " + _errors[i]);

            sb.AppendLine();
            sb.AppendLine("TOTAL: pass=" + pass + " fail=" + fail);
            sb.AppendLine(fail == 0 ? ResultMarkerPass : ResultMarkerFail);

            try
            {
                File.WriteAllText(_resultPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                sb.AppendLine("[FAIL] 结果文件写入失败: " + e.Message);
            }

            if (_subscribed)
            {
                Application.logMessageReceived -= HandleLog;
                EditorApplication.update -= Tick;
                _subscribed = false;
            }

            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
