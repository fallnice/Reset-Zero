using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Enemy;
using Enemy.Navigation;
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

        private sealed class EnemySample
        {
            public EnemyBrain Brain;
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
            public int NavFailedFrames;                         // 导航处于失败状态的帧数
            public EnemyNavigationFailure LastNavFailure;       // 最近一次导航失败原因
            public float MoveDirSum;                            // 输出方向模长累计（0=一直在原地磨）
            public float EndDistanceToPlayer = -1f;             // 追击阶段结束时与玩家的距离
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

        static PlayModeSmokeCheck()
        {
            // 域重载后（进入 PlayMode 的那一刻）由 [InitializeOnLoad] 触发，把流程续上
            if (!SessionState.GetBool(KeyRunning, false)) return;
            _scenePath = SessionState.GetString(KeyScene, "");
            _resultPath = SessionState.GetString(KeyResult, "");
            _startTime = SessionState.GetFloat(KeyStart, 0f);
            _phaseStart = SessionState.GetFloat(KeyPhaseStart, 0f);
            _phase = SessionState.GetInt(KeyPhase, 0);
            _subscribed = true;
            Application.logMessageReceived += HandleLog;
            EditorApplication.update += Tick;
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
            if (_errors.Count < 20) _errors.Add(type + ": " + condition);
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
                    // 报告必须在退出 PlayMode 之前写完：退出会触发域重载，采样数据会一起没
                    Finish(null);
                    break;
            }
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
                _samples.Add(new EnemySample
                {
                    Brain = brains[i],
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

                if (s.Brain.NavigationStatus == EnemyNavigationStatus.Failed) s.NavFailedFrames++;
                if (s.Brain.NavigationFailure != EnemyNavigationFailure.None) s.LastNavFailure = s.Brain.NavigationFailure;
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

            // 6. GC（只记录：编辑器自身也分配，绝对值不能当证据）
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
