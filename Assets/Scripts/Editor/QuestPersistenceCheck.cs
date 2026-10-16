using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Combat;
using Controller;
using Core;
using DAO;
using Model;
using Role;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using View;

namespace EditorTools
{
    /// <summary>
    /// 任务持久化与 HUD 恢复的「真进程重启」验收（10-14 第四批清单第 2 项）。
    ///
    /// 为什么单独做：`QuestRuleCheck` 验的是同进程内重新 `Initialize`，
    /// 覆盖不到「Unity 进程真的退出、再启动时」这条路径——而公司端清单要的是
    /// 「退出并重进后 HUD 恢复 Active 状态与精确计数 / 完成后重进仍保持 Completed」。
    ///
    /// 做法是**连开三个 Unity 进程**，每个进程负责一个阶段，靠 %TEMP% 下的状态文件接力：
    ///   1. 准备：重置任务 → 接取 → 只推进部分进度 → 退出
    ///   2. 重进：断言状态仍是 Active、计数与上一进程一致、HUD 文本正确渲染
    ///            → 推进剩余目标 → 交付（Completed）→ 退出
    ///   3. 再重进：断言仍是 Completed、HUD 完成后清空 → 重置回未接取
    ///
    /// 用法（必须连续跑三次，**不要带 -quit**，本方法自己 Exit）：
    ///   Unity.exe -batchmode -nographics -projectPath <工程> \
    ///     -executeMethod EditorTools.QuestPersistenceCheck.RunFromCommandLine \
    ///     -logFile <日志>
    /// 结果：`%TEMP%\unity_quest_persistence_audit.log`（每个阶段追加，最后一个阶段出 RESULT）。
    /// </summary>
    [InitializeOnLoad]
    public static class QuestPersistenceCheck
    {
        private const float MaxWaitSeconds = 120f;
        private const int CollectItemId = 1001;   // QuestDefinition 目标：收集
        private const int CraftItemId = 1008;     // QuestDefinition 目标：制造
        private const int RewardItemId = 1001;    // QuestDefinition 奖励物品
        private const int RewardCount = 5;        // QuestDefinition 奖励数量

        private const string KeyRunning = "QuestPersistenceCheck.Running";
        private const string KeyPhase = "QuestPersistenceCheck.Phase";
        private const string KeyStart = "QuestPersistenceCheck.StartTime";

        private static readonly string StatePath =
            Path.Combine(Path.GetTempPath(), "unity_quest_persistence_state.txt");
        private static readonly string ReportPath =
            Path.Combine(Path.GetTempPath(), "unity_quest_persistence_audit.log");

        private static bool _subscribed;
        private static bool _finished;
        private static int _phase;         // 0 打开场景 1 等待就绪并执行
        private static double _startTime;

        static QuestPersistenceCheck()
        {
            // 进出 PlayMode 会触发域重载，静态字段全没、update 订阅也会丢，这里续上
            if (!SessionState.GetBool(KeyRunning, false)) return;
            _startTime = SessionState.GetFloat(KeyStart, 0f);
            _phase = SessionState.GetInt(KeyPhase, 0);
            Subscribe();
        }

        /// <summary> 命令行入口：执行当前阶段，然后由外部再拉起一次本进程跑下一阶段。 </summary>
        public static void RunFromCommandLine()
        {
            _startTime = EditorApplication.timeSinceStartup;
            SessionState.SetFloat(KeyStart, (float)_startTime);
            SessionState.SetInt(KeyPhase, 0);
            SessionState.SetBool(KeyRunning, true);
            _phase = 0;
            _finished = false;
            Subscribe();
            Tick();
        }

        private static void Subscribe()
        {
            if (_subscribed) return;
            EditorApplication.update += Tick;
            _subscribed = true;
        }

        private static void Tick()
        {
            if (_finished) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _startTime > MaxWaitSeconds)
            {
                Finish("等待 GameRoot / 任务控制器就绪超时");
                return;
            }

            switch (_phase)
            {
                case 0:
                    string scene = FindFirstScene();
                    if (string.IsNullOrEmpty(scene))
                    {
                        Finish("没有可运行的场景（Assets/Scenes 下没有 .unity）");
                        return;
                    }
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                    SetPhase(1);
                    EditorApplication.EnterPlaymode();
                    break;

                case 1:
                    if (!(EditorApplication.isPlaying && !EditorApplication.isPaused)) return;

                    GameRoot root = GameRoot.Instance;
                    if (root == null) return;
                    if (root.QuestController == null) return;
                    if (root.QuestController.Definition == null) return;
                    if (SqliteManager.Instance == null) return;
                    if (!SqliteManager.Instance.IsReady) return;

                    ExecuteStage();
                    break;
            }
        }

        private static void ExecuteStage()
        {
            List<string> lines = new List<string>();
            int stage = ReadState(out int[] expected, out int cumPass, out int cumFail);

            switch (stage)
            {
                case 1:
                    RunPrepareStage(lines, ref cumPass, ref cumFail);
                    break;
                case 2:
                    RunPartialVerifyStage(lines, expected, ref cumPass, ref cumFail);
                    break;
                default:
                    RunCompletedVerifyStage(lines, ref cumPass, ref cumFail);
                    break;
            }

            Finish(lines, stage, cumPass, cumFail);
        }

        /// <summary> 阶段 1：在同一个进程里接取任务，只推进部分进度就退出。 </summary>
        private static void RunPrepareStage(List<string> lines, ref int pass, ref int fail)
        {
            QuestController controller = GameRoot.Instance.QuestController;
            QuestDefinition definition = controller.Definition;
            lines.Add("=== 阶段 1/3：首进程接取并推进部分进度 ===");

            ResetQuest(controller, definition);
            bool accepted = controller.TryAccept();
            AddResult(lines, accepted,
                "接取成功，状态=" + controller.State,
                "接取失败，状态=" + controller.State, ref pass, ref fail);

            EventBus.Emit(EventName.Pickup_Completed, null, CollectItemId, 1);
            int[] progress = controller.Progress;
            bool partial = progress.Length > 0 && progress[0] == 1
                && !ObjectivesAllComplete(controller, definition);
            AddResult(lines, partial,
                "仅推进收集目标，进度=" + DescribeProgress(progress)
                    + "（其余目标留作下一进程验证）",
                "部分进度推进失败，进度=" + DescribeProgress(progress), ref pass, ref fail);

            WriteState(2, progress, pass, fail);
            lines.Add("[INFO] 进程即将退出，下一进程负责验证「重进后是否恢复这些计数」");
        }

        /// <summary> 阶段 2：进程重启后 HUD 与计数是否完整恢复，然后完成交付。 </summary>
        private static void RunPartialVerifyStage(
            List<string> lines, int[] expected, ref int pass, ref int fail)
        {
            QuestController controller = GameRoot.Instance.QuestController;
            QuestDefinition definition = controller.Definition;
            lines.Add("=== 阶段 2/3：Unity 进程重启后（上一进程只推进了部分进度） ===");

            bool stateOk = controller.State == QuestState.Active;
            bool progressOk = MatchesProgress(controller.Progress, expected);
            AddResult(lines, stateOk && progressOk,
                "跨进程恢复 Active 与精确计数：当前进度=" + DescribeProgress(controller.Progress)
                    + "，期望=" + DescribeProgress(expected),
                "重进后状态不符：状态=" + controller.State
                    + "，进度=" + DescribeProgress(controller.Progress)
                    + "，期望=" + DescribeProgress(expected), ref pass, ref fail);

            string hud = ReadQuestHudText();
            bool hudOk = !string.IsNullOrEmpty(hud) && definition.Title != null
                && hud.Contains(definition.Title)
                && ContainsCounts(hud, expected, definition);
            AddResult(lines, hudOk,
                "重进后 HUD 渲染正确：「" + FirstLine(hud) + "」",
                "重进后 HUD 与存档不一致：hud=\"" + FirstLine(hud)
                    + "\"，期望进度=" + DescribeProgress(expected), ref pass, ref fail);

            // 推进剩余目标：制造 + 击杀（只发领域事件，不再依靠上一进程的运行时数据）
            EventBus.Emit(EventName.Craft_Success, null, CraftItemId, 1);
            EmitPlayerKill();
            bool ready = controller.IsReadyToTurnIn;
            AddResult(lines, ready,
                "推进剩余目标后进入可交付状态，进度=" + DescribeProgress(controller.Progress),
                "剩余目标未达成，状态=" + controller.State
                    + "，进度=" + DescribeProgress(controller.Progress), ref pass, ref fail);

            bool turnedIn = ready && controller.TryTurnIn();
            AddResult(lines, turnedIn && controller.State == QuestState.Completed,
                "交付完成并写入 Completed，状态=" + controller.State,
                "交付失败，状态=" + controller.State, ref pass, ref fail);

            RemoveReward();   // 交付会真发奖励，退回去以免反复运行把背包刷满
            WriteState(3, controller.Progress, pass, fail);
            lines.Add("[INFO] 进程即将退出，下一进程负责验证「重进后是否仍保持 Completed」");
        }

        /// <summary> 阶段 3：交付后再重进是否仍保持 Completed，HUD 按设计清空。 </summary>
        private static void RunCompletedVerifyStage(List<string> lines, ref int pass, ref int fail)
        {
            QuestController controller = GameRoot.Instance.QuestController;
            QuestDefinition definition = controller.Definition;
            lines.Add("=== 阶段 3/3：交付后 Unity 进程重启 ===");

            AddResult(lines, controller.State == QuestState.Completed,
                "跨进程保持 Completed，状态=" + controller.State,
                "重进后丢失完成状态，状态=" + controller.State, ref pass, ref fail);

            string hud = ReadQuestHudText();
            AddResult(lines, string.IsNullOrEmpty(hud),
                "完成后 HUD 按设计清空（NotAccepted / Completed 不渲染任务文本）",
                "完成后 HUD 仍有残留文本：\"" + FirstLine(hud) + "\"", ref pass, ref fail);

            ResetQuest(controller, definition);
            lines.Add("[INFO] 已重置回未接取，验收状态文件已清除，下一轮可重新开始");
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }

        // ── 辅助 ────────────────────────────────────────────

        private static void ResetQuest(QuestController controller, QuestDefinition definition)
        {
            try
            {
                new QuestDao().Save(definition.QuestId, definition.DefinitionVersion,
                    QuestState.NotAccepted, new int[definition.Objectives.Count]);
                controller.Initialize(definition);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuestPersistenceCheck] 重置任务存档失败: " + exception.Message);
            }
        }

        /// <summary> 发一条「玩家击杀敌对角色」的死亡事实（不真的杀死，只验任务是否据此计数）。 </summary>
        private static void EmitPlayerKill()
        {
            CharacterRoot player = null;
            CharacterRoot victim = null;
            CharacterRoot[] roots = UnityEngine.Object.FindObjectsOfType<CharacterRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                CharacterRoot candidate = roots[i];
                if (candidate == null || candidate.IsPlayerControlled) continue;
                if (candidate.Faction != Faction.Enemy || candidate.Health == null
                    || candidate.Health.IsDead)
                    continue;
                victim = candidate;
                break;
            }
            if (victim == null) return;

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].IsPlayerControlled) { player = roots[i]; break; }
            }
            if (player == null) return;

            DamageContext context = new DamageContext
            {
                amount = 9999f,
                attacker = player.gameObject,
                sourceFaction = Faction.Player,
                hitPoint = victim.transform.position
            };
            EventBus.Emit(EventName.Character_Died, victim, context);
        }

        private static void RemoveReward()
        {
            GameRoot root = GameRoot.Instance;
            if (root == null || root.Inventory == null) return;
            root.Inventory.RemoveItem(RewardItemId, RewardCount);
        }

        /// <summary> 反射读 QuestView 的 TMP 文本（字段是私有的，没有公开 getter）。 </summary>
        private static string ReadQuestHudText()
        {
            QuestView[] views = UnityEngine.Object.FindObjectsOfType<QuestView>(true);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null) continue;
                FieldInfo field = typeof(QuestView).GetField(
                    "questText", BindingFlags.Instance | BindingFlags.NonPublic);
                TMP_Text text = field != null ? field.GetValue(views[i]) as TMP_Text : null;
                if (text != null && !string.IsNullOrEmpty(text.text)) return text.text;
            }
            return null;
        }

        private static bool ObjectivesAllComplete(
            QuestController controller, QuestDefinition definition)
        {
            int[] progress = controller.Progress;
            for (int i = 0; i < definition.Objectives.Count; i++)
            {
                if (i >= progress.Length) return false;
                if (progress[i] < definition.Objectives[i].RequiredCount) return false;
            }
            return true;
        }

        private static bool MatchesProgress(int[] actual, int[] expected)
        {
            if (actual == null || expected == null) return false;
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++)
                if (actual[i] != expected[i]) return false;
            return true;
        }

        private static bool ContainsCounts(
            string hud, int[] expected, QuestDefinition definition)
        {
            for (int i = 0; i < expected.Length && i < definition.Objectives.Count; i++)
            {
                string token = expected[i].ToString(CultureInfo.InvariantCulture) + "/"
                    + definition.Objectives[i].RequiredCount.ToString(CultureInfo.InvariantCulture);
                if (!hud.Contains(token)) return false;
            }
            return true;
        }

        private static string DescribeProgress(int[] progress)
        {
            return progress == null ? "null" : "[" + string.Join(",", Array.ConvertAll(
                progress, v => v.ToString(CultureInfo.InvariantCulture))) + "]";
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int index = text.IndexOf('\n');
            string first = index >= 0 ? text.Substring(0, index) : text;
            return first.Length > 60 ? first.Substring(0, 60) + "…" : first;
        }

        private static void AddResult(
            List<string> lines, bool ok, string passText, string failText,
            ref int pass, ref int fail)
        {
            lines.Add((ok ? "[PASS] " : "[FAIL] ") + (ok ? passText : failText));
            if (ok) pass++; else fail++;
        }

        private static void SetPhase(int phase)
        {
            _phase = phase;
            SessionState.SetInt(KeyPhase, phase);
        }

        private static string FindFirstScene()
        {
            // 必须用 Assets/Scenes 下的场景：素材包里还有一堆演示场景，
            // 它们没有 GameRoot，跑进去只会卡在「等待 GameRoot 就绪」超时
            string directory = Path.Combine(Application.dataPath, "Scenes");
            if (Directory.Exists(directory))
            {
                string[] scenes = Directory.GetFiles(directory, "*.unity");
                if (scenes.Length > 0)
                    return "Assets/Scenes/" + Path.GetFileName(scenes[0]);
            }
            return null;
        }

        // ── 状态文件接力 ────────────────────────────────────

        private static int ReadState(out int[] expected, out int cumPass, out int cumFail)
        {
            expected = null;
            cumPass = 0;
            cumFail = 0;
            if (!File.Exists(StatePath)) return 1;

            try
            {
                string[] rows = File.ReadAllLines(StatePath);
                if (rows.Length < 4) return 1;
                cumPass = int.Parse(rows[2], CultureInfo.InvariantCulture);
                cumFail = int.Parse(rows[3], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(rows[1]))
                {
                    string[] parts = rows[1].Split(',');
                    expected = Array.ConvertAll(parts,
                        v => int.Parse(v.Trim(), CultureInfo.InvariantCulture));
                }
                return int.Parse(rows[0], CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuestPersistenceCheck] 状态文件损坏，从阶段 1 重来: "
                    + exception.Message);
                return 1;
            }
        }

        private static void WriteState(int nextStage, int[] progress, int cumPass, int cumFail)
        {
            try
            {
                string line = progress == null
                    ? string.Empty
                    : string.Join(",", Array.ConvertAll(progress,
                        v => v.ToString(CultureInfo.InvariantCulture)));
                File.WriteAllLines(StatePath, new[]
                {
                    nextStage.ToString(CultureInfo.InvariantCulture),
                    line,
                    cumPass.ToString(CultureInfo.InvariantCulture),
                    cumFail.ToString(CultureInfo.InvariantCulture)
                }, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuestPersistenceCheck] 写入状态文件失败: " + exception.Message);
            }
        }

        // ── 收尾 ──────────────────────────────────────────

        private static void Finish(string fatalReason)
        {
            List<string> lines = new List<string>();
            ReadState(out int[] ignored, out int cumPass, out int cumFail);
            lines.Add("[FAIL] " + fatalReason);
            cumFail++;
            Finish(lines, 0, cumPass, cumFail);
        }

        private static void Finish(List<string> lines, int stage, int cumPass, int cumFail)
        {
            if (_finished) return;
            _finished = true;
            SessionState.SetBool(KeyRunning, false);
            SessionState.EraseInt(KeyPhase);

            if (_subscribed)
            {
                EditorApplication.update -= Tick;
                _subscribed = false;
            }

            StringBuilder sb = new StringBuilder();
            if (stage <= 1)
            {
                sb.AppendLine("=== 任务持久化与 HUD 恢复（真进程重启，连续三个 Unity 进程） ===");
                sb.AppendLine();
            }
            for (int i = 0; i < lines.Count; i++) sb.AppendLine(lines[i]);

            if (stage >= 3)
            {
                sb.AppendLine();
                sb.AppendLine("TOTAL: pass=" + cumPass + " fail=" + cumFail);
                sb.AppendLine(cumFail == 0
                    ? "QUEST_PERSISTENCE_RESULT: PASS" : "QUEST_PERSISTENCE_RESULT: FAIL");
            }

            bool written = true;
            try
            {
                if (stage <= 1) File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
                else File.AppendAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                written = false;
                Debug.LogError("[QuestPersistenceCheck] 结果文件写入失败: " + exception.Message);
            }

            EditorApplication.Exit(cumFail == 0 && written ? 0 : 1);
        }
    }
}
