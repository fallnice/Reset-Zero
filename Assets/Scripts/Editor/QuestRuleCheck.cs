#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using Combat;
using Controller;
using Core;
using DAO;
using Dao;
using Model;
using Mono.Data.Sqlite;
using Role;
using TMPro;
using UnityEditor;
using UnityEngine;
using View;
using Object = UnityEngine.Object;

namespace EditorTools
{
    /// <summary> 无场景验证单任务接取、三类进度、交付奖励与存档失败边界。 </summary>
    public static class QuestRuleCheck
    {
        private static readonly string ResultPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "unity_quest_rule_audit.log");

        /// <summary> 从命令行运行任务规则验收并返回进程退出码。 </summary>
        public static void RunFromCommandLine()
        {
            bool passed = Run(out string report);
            System.IO.File.WriteAllText(ResultPath, report, Encoding.UTF8);
            EditorApplication.Exit(passed ? 0 : 1);
        }

        /// <summary> 执行任务控制器的核心生命周期验收。 </summary>
        public static bool Run(out string report)
        {
            var builder = new StringBuilder();
            int passCount = 0;
            int failCount = 0;
            QuestDefinition definition = CreateDefinition();
            var victimObject = new GameObject("QuestRuleEnemy");
            var attackerObject = new GameObject("QuestRulePlayer");
            try
            {
                CharacterRoot victim = victimObject.AddComponent<CharacterRoot>();
                CharacterRoot attacker = attackerObject.AddComponent<CharacterRoot>();
                SetCharacterFaction(victim, Faction.Enemy);
                SetCharacterFaction(attacker, Faction.Player);
                SetPlayerControlled(attacker, true);

                var repository = new MemoryQuestRepository();
                var inventory = new MemoryInventory();
                var controller = new QuestController(inventory, repository);
                Assert(controller.Initialize(definition), "合法任务定义可初始化", builder,
                    ref passCount, ref failCount);
                Assert(controller.TryAccept() && controller.State == QuestState.Active,
                    "任务接取后进入 Active 并持久化", builder, ref passCount, ref failCount);

                EventBus.Emit(EventName.Pickup_Completed, null, 1001, 1);
                controller.Dispose();
                controller = new QuestController(inventory, repository);
                Assert(controller.Initialize(definition)
                    && controller.State == QuestState.Active
                    && controller.Progress[0] == 1,
                    "中途重载后恢复 Active 状态与目标进度", builder, ref passCount, ref failCount);
                VerifyQuestViewLifecycle(controller, definition, builder, ref passCount, ref failCount);

                EventBus.Emit(EventName.Pickup_Completed, null, 1001, 1);
                EventBus.Emit(EventName.Craft_Success, 10, 2001, 1);
                VerifyInvalidKillsIgnored(controller, victim, attacker, attackerObject, builder,
                    ref passCount, ref failCount);
                HealthController victimHealth = AddHealthAndSubscribe(victim);
                victimHealth.TakeDamage(new DamageContext
                {
                    amount = 100f,
                    attacker = attackerObject,
                    sourceFaction = Faction.Player
                });
                Assert(controller.State == QuestState.ReadyToTurnIn,
                    "收集、制造和玩家击杀共同完成任务", builder, ref passCount, ref failCount);

                Assert(controller.TryTurnIn() && controller.State == QuestState.Completed
                    && inventory.GetItemTotalCount(3001) == 1
                    && repository.SavedState == QuestState.Completed,
                    "奖励入包与 Completed 存档原子提交", builder, ref passCount, ref failCount);
                controller.Dispose();

                var rollbackRepository = new MemoryQuestRepository { FailCompletedSave = true };
                var rollbackInventory = new MemoryInventory();
                var rollbackController = new QuestController(rollbackInventory, rollbackRepository);
                rollbackController.Initialize(definition);
                rollbackController.TryAccept();
                EventBus.Emit(EventName.Pickup_Completed, null, 1001, 2);
                EventBus.Emit(EventName.Craft_Success, 10, 2001, 1);
                victimHealth.ResetHealth();
                victimHealth.TakeDamage(new DamageContext
                {
                    amount = 100f,
                    attacker = attackerObject,
                    sourceFaction = Faction.Player
                });
                Assert(!rollbackController.TryTurnIn()
                    && rollbackController.State == QuestState.ReadyToTurnIn
                    && rollbackInventory.GetItemTotalCount(3001) == 0,
                    "完成状态写库失败时奖励与状态同时回滚", builder, ref passCount, ref failCount);
                rollbackController.Dispose();

                var reloaded = new QuestController(inventory, repository);
                Assert(reloaded.Initialize(definition) && reloaded.State == QuestState.Completed,
                    "重新初始化后恢复 Completed 状态", builder, ref passCount, ref failCount);
                reloaded.Dispose();

                var failingRepository = new MemoryQuestRepository { ShouldFailSave = true };
                var failingController = new QuestController(new MemoryInventory(), failingRepository);
                bool initialized = failingController.Initialize(definition);
                Assert(initialized && !failingController.TryAccept()
                    && failingController.State == QuestState.NotAccepted,
                    "存档失败时接取不改变运行时状态", builder, ref passCount, ref failCount);
                failingController.Dispose();

                VerifyDefinitionVersionBoundary(definition, builder, ref passCount, ref failCount);
                VerifyProductionSqliteRollback(builder, ref passCount, ref failCount);
            }
            catch (Exception exception)
            {
                failCount++;
                builder.AppendLine("[FAIL] 未处理异常: " + exception);
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(victimObject);
                Object.DestroyImmediate(attackerObject);
            }

            builder.AppendLine($"QUEST_RULE_AUDIT_RESULT: {(failCount == 0 ? "PASS" : "FAIL")}");
            builder.AppendLine($"TOTAL: pass={passCount} fail={failCount}");
            report = builder.ToString();
            return failCount == 0;
        }

        /// <summary> 验证任务 HUD 在启用、禁用和重新启用时正确同步状态且不重复订阅。 </summary>
        private static void VerifyQuestViewLifecycle(
            QuestController controller,
            QuestDefinition definition,
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            var viewObject = new GameObject("QuestRuleView");
            viewObject.SetActive(false);
            try
            {
                TextMeshProUGUI text = viewObject.AddComponent<TextMeshProUGUI>();
                QuestView view = viewObject.AddComponent<QuestView>();
                SetPrivateField(view, "questText", text);
                InvokePrivateMethod(view, "OnEnable");
                string activeText = text.text ?? string.Empty;
                bool activeRendered = activeText.Contains(definition.Title)
                    && activeText.Contains("1/2")
                    && activeText.Contains("0/1");

                InvokePrivateMethod(view, "OnDisable");
                string disabledText = text.text ?? string.Empty;
                EventBus.Emit(EventName.Quest_Changed, definition, QuestState.ReadyToTurnIn,
                    new[] { 2, 1, 1 });
                bool disabledIgnored = text.text == disabledText;

                InvokePrivateMethod(view, "OnEnable");
                string reenabledText = text.text ?? string.Empty;
                bool reenabledRendered = reenabledText.Contains(definition.Title)
                    && reenabledText.Contains("1/2")
                    && !reenabledText.Contains("可交付");
                Assert(activeRendered && disabledIgnored && reenabledRendered,
                    "任务 HUD 启用查询、禁用退订与重新启用恢复正确", builder,
                    ref passCount, ref failCount);
                InvokePrivateMethod(view, "OnDisable");
                Object.DestroyImmediate(view);
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }

        /// <summary> 创建覆盖三类目标和物品奖励的临时任务定义。 </summary>
        private static QuestDefinition CreateDefinition()
        {
            QuestDefinition definition = ScriptableObject.CreateInstance<QuestDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("questId").stringValue = "quest_rule_test";
            serialized.FindProperty("title").stringValue = "规则验收任务";
            serialized.FindProperty("rewardItemId").intValue = 3001;
            serialized.FindProperty("rewardCount").intValue = 1;

            SerializedProperty objectives = serialized.FindProperty("objectives");
            objectives.arraySize = 3;
            ConfigureObjective(objectives.GetArrayElementAtIndex(0), QuestObjectiveType.Collect, 1001, 2);
            ConfigureObjective(objectives.GetArrayElementAtIndex(1), QuestObjectiveType.Craft, 2001, 1);
            ConfigureObjective(objectives.GetArrayElementAtIndex(2), QuestObjectiveType.Kill, 0, 1);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary> 配置一个内联序列化任务目标。 </summary>
        private static void ConfigureObjective(
            SerializedProperty objective,
            QuestObjectiveType type,
            int targetId,
            int count)
        {
            objective.FindPropertyRelative("type").enumValueIndex = (int)type;
            objective.FindPropertyRelative("targetId").intValue = targetId;
            objective.FindPropertyRelative("requiredCount").intValue = count;
            objective.FindPropertyRelative("description").stringValue = type.ToString();
        }

        /// <summary> 设置测试角色阵营。 </summary>
        private static void SetCharacterFaction(CharacterRoot character, Faction faction)
        {
            var serialized = new SerializedObject(character);
            serialized.FindProperty("faction").enumValueIndex = (int)faction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary> 显式设置测试角色的玩家控制身份。 </summary>
        private static void SetPlayerControlled(CharacterRoot character, bool isPlayerControlled)
        {
            var serialized = new SerializedObject(character);
            serialized.FindProperty("isPlayerControlled").boolValue = isPlayerControlled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary> 验证不满足玩家击杀归因条件的死亡事实不会推进任务。 </summary>
        private static void VerifyInvalidKillsIgnored(
            QuestController controller,
            CharacterRoot victim,
            CharacterRoot attacker,
            GameObject attackerObject,
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            int before = controller.Progress[2];
            var neutralVictimObject = new GameObject("QuestRuleNeutralVictim");
            var sourceOnlyObject = new GameObject("QuestRuleSourceOnly");
            try
            {
                CharacterRoot neutralVictim = neutralVictimObject.AddComponent<CharacterRoot>();
                SetCharacterFaction(neutralVictim, Faction.Neutral);
                EventBus.Emit(EventName.Character_Died, neutralVictim, CreateLethalContext(attackerObject));
                EventBus.Emit(EventName.Character_Died, victim, new DamageContext
                {
                    amount = 100f,
                    attacker = attackerObject,
                    sourceFaction = Faction.Enemy
                });
                EventBus.Emit(EventName.Character_Died, victim, CreateLethalContext(sourceOnlyObject));
                SetPlayerControlled(attacker, false);
                EventBus.Emit(EventName.Character_Died, victim, CreateLethalContext(attackerObject));
                SetPlayerControlled(attacker, true);
                SetCharacterFaction(attacker, Faction.Enemy);
                EventBus.Emit(EventName.Character_Died, victim, CreateLethalContext(attackerObject));
                SetCharacterFaction(attacker, Faction.Player);

                Assert(controller.Progress[2] == before,
                    "无效受害者、来源或攻击者不会推进击杀目标", builder,
                    ref passCount, ref failCount);
            }
            finally
            {
                Object.DestroyImmediate(neutralVictimObject);
                Object.DestroyImmediate(sourceOnlyObject);
            }
        }

        /// <summary> 创建玩家来源的致死伤害上下文。 </summary>
        private static DamageContext CreateLethalContext(GameObject attacker)
        {
            return new DamageContext
            {
                amount = 100f,
                attacker = attacker,
                sourceFaction = Faction.Player
            };
        }

        /// <summary> 挂载生命组件并补充 CharacterRoot 在正常 OnEnable 中建立的死亡事件订阅。 </summary>
        private static HealthController AddHealthAndSubscribe(CharacterRoot character)
        {
            HealthController health = character.gameObject.AddComponent<HealthController>();
            health.maxHealth = 10f;
            health.ResetHealth();

            FieldInfo healthField = typeof(CharacterRoot).GetField(
                "health", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo unsubscribeMethod = typeof(CharacterRoot).GetMethod(
                "UnsubscribeRuntimeEvents", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo subscribeMethod = typeof(CharacterRoot).GetMethod(
                "SubscribeRuntimeEvents", BindingFlags.Instance | BindingFlags.NonPublic);
            if (healthField == null || unsubscribeMethod == null || subscribeMethod == null)
                throw new MissingMemberException("CharacterRoot 生命订阅测试接入点不存在");

            unsubscribeMethod.Invoke(character, null);
            healthField.SetValue(character, health);
            subscribeMethod.Invoke(character, null);
            return health;
        }

        /// <summary> 验证任务定义版本变化后旧进度不会被错误套用。 </summary>
        private static void VerifyDefinitionVersionBoundary(
            QuestDefinition definition,
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            var repository = new MemoryQuestRepository();
            repository.Save(definition.QuestId, definition.DefinitionVersion + 1,
                QuestState.Completed, new[] { 2, 1, 1 });
            var controller = new QuestController(new MemoryInventory(), repository);
            bool initialized = controller.Initialize(definition);
            Assert(initialized && controller.State == QuestState.NotAccepted
                && IsZeroProgress(controller.Progress),
                "定义版本失配时旧任务进度安全重置", builder, ref passCount, ref failCount);
            controller.Dispose();
        }

        /// <summary> 验证生产 QuestDao 与背包槽位写入由真实 SQLite 事务共同回滚。 </summary>
        private static void VerifyProductionSqliteRollback(
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            GameObject managerObject = null;
            SqliteConnection connection = null;
            try
            {
                managerObject = new GameObject("QuestRuleSqliteManager");
                SqliteManager manager = managerObject.AddComponent<SqliteManager>();
                connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                SetPrivateField(manager, "_connection", connection);

                // 关键：生产组件（BagController / 各 DAO）一律走 `SqliteManager.Instance`，
                // 只给临时组件注入连接是不够的——必须让单例也指向这个实例，否则 DAO 里
                // `SqliteManager.Instance.ExecuteQuery` 会拿到没连接的实例并抛空引用。
                SetPrivateStaticField(typeof(SqliteManager),
                    "<Instance>k__BackingField", manager);

                CreateProductionTestTables(manager);

                var bag = new BagController();
                bag.Init();
                var repository = new QuestDao();
                repository.EnsureTable();
                bool nestedCallbackCalled = false;
                bool nestedAdded = false;
                InventoryOperationResult nestedResult = InventoryOperationResult.Success;
                manager.RunInTransaction(() =>
                {
                    nestedAdded = bag.TryAddItemAtomic(3001, 1, () => nestedCallbackCalled = true,
                        out nestedResult);
                });
                Assert(!nestedAdded && !nestedCallbackCalled
                    && nestedResult == InventoryOperationResult.PersistenceFailed
                    && bag.GetItemTotalCount(3001) == 0
                    && ReadScalarInt(manager,
                        "SELECT item_count FROM PlayerBag WHERE slot_id=1") == 0,
                    "生产背包拒绝嵌套原子奖励且不执行回调", builder,
                    ref passCount, ref failCount);

                bool added = bag.TryAddItemAtomic(3001, 1, () =>
                {
                    repository.Save("quest_sqlite_rollback", 1, QuestState.Completed, new[] { 1 });
                    throw new InvalidOperationException("模拟完成状态后的事务失败");
                }, out InventoryOperationResult result);

                bool rolledBack = !added && result == InventoryOperationResult.PersistenceFailed
                    && bag.GetItemTotalCount(3001) == 0
                    && ReadScalarInt(manager,
                        "SELECT item_count FROM PlayerBag WHERE slot_id=1") == 0
                    && ReadScalarInt(manager,
                        "SELECT COUNT(*) FROM QuestProgress WHERE QuestId='quest_sqlite_rollback'") == 0;
                Assert(rolledBack, "生产 SQLite 中奖励与任务完成状态共同回滚", builder,
                    ref passCount, ref failCount);
            }
            catch (TypeInitializationException exception)
            {
                builder.AppendLine("[SKIP] 生产 SQLite 回滚验收：当前环境缺少原生 SQLite 库（"
                    + exception.GetBaseException().Message + "）");
            }
            catch (DllNotFoundException exception)
            {
                builder.AppendLine("[SKIP] 生产 SQLite 回滚验收：当前环境缺少原生 SQLite 库（"
                    + exception.Message + "）");
            }
            catch (Exception exception)
            {
                // 带上堆栈：只在完整资产机器上才跑得到这一段，没有堆栈没法定位
                Assert(false, "生产 SQLite 回滚验收异常: " + exception.Message
                    + " | 堆栈: " + exception.StackTrace, builder,
                    ref passCount, ref failCount);
            }
            finally
            {
                connection?.Close();
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        /// <summary> 创建生产 DAO 所需的最小内存数据库结构。 </summary>
        private static void CreateProductionTestTables(SqliteManager manager)
        {
            manager.ExecuteNonQuery(
                "CREATE TABLE ItemConfig (id INTEGER PRIMARY KEY, name TEXT, type TEXT, "
                + "description TEXT, max_stack INTEGER)");
            manager.ExecuteNonQuery(
                "CREATE TABLE PlayerBag (slot_id INTEGER PRIMARY KEY, item_id INTEGER, item_count INTEGER)");
            manager.ExecuteNonQuery(
                "INSERT INTO ItemConfig VALUES (3001, '任务奖励', 'Quest', '', 10)");
            for (int i = 1; i <= 30; i++)
                manager.ExecuteNonQuery($"INSERT INTO PlayerBag VALUES ({i}, 0, 0)");
        }

        /// <summary> 通过反射设置私有静态字段（用于把单例指向测试用的实例）。 </summary>
        private static void SetPrivateStaticField(System.Type type, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = type.GetField(
                fieldName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new System.MissingFieldException(type.Name, fieldName);
            field.SetValue(null, value);
        }

        /// <summary> 通过反射设置仅供生产组件维护的私有测试依赖。 </summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        /// <summary> 显式调用 Unity 私有生命周期方法，供无场景编辑器验收使用。 </summary>
        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(target.GetType().Name, methodName);
            method.Invoke(target, null);
        }

        /// <summary> 读取单个整数查询结果。 </summary>
        private static int ReadScalarInt(SqliteManager manager, string sql)
        {
            using SqliteDataReader reader = manager.ExecuteQuery(sql);
            return reader.Read() ? Convert.ToInt32(reader.GetValue(0)) : 0;
        }

        /// <summary> 检查目标进度是否全部为零。 </summary>
        private static bool IsZeroProgress(int[] progress)
        {
            if (progress == null) return false;
            for (int i = 0; i < progress.Length; i++)
            {
                if (progress[i] != 0) return false;
            }
            return true;
        }

        /// <summary> 记录单项断言结果。 </summary>
        private static void Assert(
            bool condition,
            string message,
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            builder.AppendLine((condition ? "[PASS] " : "[FAIL] ") + message);
            if (condition) passCount++; else failCount++;
        }

        /// <summary> 内存任务仓储，用于验证控制器存档行为。 </summary>
        private sealed class MemoryQuestRepository : IQuestRepository
        {
            public bool ShouldFailSave { get; set; }
            public bool FailCompletedSave { get; set; }
            public QuestState SavedState { get; private set; } = QuestState.NotAccepted;
            private int _savedDefinitionVersion;
            private int[] _savedProgress;
            private bool _hasSave;

            public void EnsureTable()
            {
            }

            public QuestProgressData Load(string questId)
            {
                return _hasSave
                    ? new QuestProgressData
                    {
                        DefinitionVersion = _savedDefinitionVersion,
                        State = SavedState,
                        Progress = (int[])_savedProgress.Clone()
                    }
                    : null;
            }

            public void Save(
                string questId,
                int definitionVersion,
                QuestState state,
                int[] progress)
            {
                if (ShouldFailSave || (FailCompletedSave && state == QuestState.Completed))
                    throw new InvalidOperationException("模拟存档失败");
                SavedState = state;
                _savedDefinitionVersion = definitionVersion;
                _savedProgress = (int[])progress.Clone();
                _hasSave = true;
            }
        }

        /// <summary> 内存背包，用于验证奖励原子操作。 </summary>
        private sealed class MemoryInventory : IInventory
        {
            private readonly Dictionary<int, int> _items = new Dictionary<int, int>();

            public InventoryOperationResult LastAddResult { get; private set; }

            public bool AddItem(int itemId, int count)
            {
                return TryAddItem(itemId, count, out _);
            }

            public bool TryAddItem(int itemId, int count, out InventoryOperationResult result)
            {
                if (itemId <= 0 || count <= 0)
                {
                    result = InventoryOperationResult.InvalidArgument;
                    LastAddResult = result;
                    return false;
                }
                _items[itemId] = GetItemTotalCount(itemId) + count;
                result = InventoryOperationResult.Success;
                LastAddResult = result;
                return true;
            }

            public bool RemoveItem(int itemId, int count)
            {
                int current = GetItemTotalCount(itemId);
                if (current < count) return false;
                _items[itemId] = current - count;
                return true;
            }

            public int GetItemTotalCount(int itemId)
            {
                return _items.TryGetValue(itemId, out int count) ? count : 0;
            }

            public bool HasItems(int itemId, int count)
            {
                return GetItemTotalCount(itemId) >= count;
            }

            public bool CanAddItem(int itemId, int count)
            {
                return itemId > 0 && count > 0;
            }

            public bool TryCraft(RecipeInfo recipe, out string failReason)
            {
                failReason = "测试未使用制造接口";
                return false;
            }

            public bool TryAddItemAtomic(
                int itemId,
                int count,
                Action persistAdditionalChanges,
                out InventoryOperationResult result)
            {
                int before = GetItemTotalCount(itemId);
                try
                {
                    persistAdditionalChanges();
                    _items[itemId] = before + count;
                    result = InventoryOperationResult.Success;
                    LastAddResult = result;
                    return true;
                }
                catch
                {
                    _items[itemId] = before;
                    result = InventoryOperationResult.PersistenceFailed;
                    LastAddResult = result;
                    return false;
                }
            }
        }
    }
}
#endif
