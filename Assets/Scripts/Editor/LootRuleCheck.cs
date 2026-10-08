#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Combat;
using Loot;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary> 无场景依赖的掉落规则验收，覆盖概率边界、数量范围与同物品合并。 </summary>
    public static class LootRuleCheck
    {
        private const string DEFAULT_LOG_FILE = "unity_loot_rule_audit.log";
        private const string RESULT_PASS = "LOOT_RULE_AUDIT_RESULT: PASS";
        private const string RESULT_FAIL = "LOOT_RULE_AUDIT_RESULT: FAIL";

        /// <summary> 从命令行运行掉落规则验收，并按结果退出 Unity。 </summary>
        public static void RunFromCommandLine()
        {
            bool passed = Run(out string report);
            string path = Path.Combine(Path.GetTempPath(), DEFAULT_LOG_FILE);
            File.WriteAllText(path, report, Encoding.UTF8);
            EditorApplication.Exit(passed ? 0 : 1);
        }

        /// <summary> 从编辑器菜单运行掉落规则验收。 </summary>
        [MenuItem("Tools/自检/掉落规则验收", false, 4)]
        public static void RunFromMenu()
        {
            bool passed = Run(out string report);
            string path = Path.Combine(Path.GetTempPath(), DEFAULT_LOG_FILE);
            File.WriteAllText(path, report, Encoding.UTF8);
            if (passed) Debug.Log($"[LootRuleCheck] 验收通过：{path}");
            else Debug.LogError($"[LootRuleCheck] 验收失败：{path}\n{report}");
        }

        /// <summary> 构造临时掉落表并执行确定性边界检查。 </summary>
        private static bool Run(out string report)
        {
            var builder = new StringBuilder();
            int passCount = 0;
            int failCount = 0;
            LootTable table = ScriptableObject.CreateInstance<LootTable>();
            try
            {
                ConfigureEntries(table, new[]
                {
                    CreateEntry(1001, 2, 4, 1f),
                    CreateEntry(1002, 1, 1, 0f),
                    CreateEntry(1001, 3, 3, 1f)
                });

                var results = new List<LootDropResult>(4);
                bool hasValidRolls = true;
                for (int i = 0; i < 64; i++)
                {
                    table.RollInto(results);
                    if (results.Count != 1 || results[0].ItemId != 1001
                        || results[0].Count < 5 || results[0].Count > 7)
                    {
                        hasValidRolls = false;
                        break;
                    }
                }
                Assert(hasValidRolls, "概率边界、数量范围与同物品合并连续 64 次正确", builder,
                    ref passCount, ref failCount);
                CheckHealthLifecycle(builder, ref passCount, ref failCount);
            }
            finally
            {
                Object.DestroyImmediate(table);
            }

            builder.AppendLine();
            builder.AppendLine($"TOTAL: pass={passCount} fail={failCount}");
            builder.AppendLine(failCount == 0 ? RESULT_PASS : RESULT_FAIL);
            report = builder.ToString();
            return failCount == 0;
        }

        /// <summary> 验证死亡每轮只触发一次、致死来源保留且复活后可开启新周期。 </summary>
        private static void CheckHealthLifecycle(StringBuilder builder, ref int passCount, ref int failCount)
        {
            var target = new GameObject("LootHealthRuleTarget");
            var attacker = new GameObject("LootHealthRuleAttacker");
            try
            {
                HealthController health = target.AddComponent<HealthController>();
                health.maxHealth = 10f;
                health.ResetHealth();
                int deathCount = 0;
                GameObject lethalAttacker = null;
                health.DiedWithContext += context =>
                {
                    deathCount++;
                    lethalAttacker = context.attacker;
                };

                var lethalHit = new DamageContext
                {
                    amount = 20f,
                    attacker = attacker,
                    sourceFaction = Faction.Enemy
                };
                health.TakeDamage(lethalHit);
                health.TakeDamage(lethalHit);
                Assert(deathCount == 1 && health.IsDead,
                    "同一生命周期重复伤害只触发一次死亡", builder, ref passCount, ref failCount);
                Assert(lethalAttacker == attacker,
                    "死亡事件保留致死攻击者上下文", builder, ref passCount, ref failCount);

                health.ResetHealth();
                health.TakeDamage(lethalHit);
                Assert(deathCount == 2,
                    "复活后死亡可开启新一轮结算", builder, ref passCount, ref failCount);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(attacker);
            }
        }

        /// <summary> 通过 SerializedObject 注入临时条目，不向运行时代码开放配置集合写权限。 </summary>
        private static void ConfigureEntries(LootTable table, LootEntry[] entries)
        {
            var serialized = new SerializedObject(table);
            SerializedProperty property = serialized.FindProperty("entries");
            property.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                LootEntry entry = entries[i];
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemId").intValue = entry.ItemId;
                element.FindPropertyRelative("minCount").intValue = entry.MinCount;
                element.FindPropertyRelative("maxCount").intValue = entry.MaxCount;
                element.FindPropertyRelative("dropChance").floatValue = entry.DropChance;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary> 创建一项临时掉落配置。 </summary>
        private static LootEntry CreateEntry(int itemId, int minCount, int maxCount, float chance)
        {
            return new LootEntry(itemId, minCount, maxCount, chance);
        }

        /// <summary> 记录单项断言结果。 </summary>
        private static void Assert(
            bool condition,
            string message,
            StringBuilder builder,
            ref int passCount,
            ref int failCount)
        {
            if (condition)
            {
                passCount++;
                builder.AppendLine("[PASS] " + message);
                return;
            }

            failCount++;
            builder.AppendLine("[FAIL] " + message);
        }
    }
}
#endif
