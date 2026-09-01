#if UNITY_EDITOR
using Mono.Data.Sqlite;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 物品表（ItemConfig）调试工具（菜单：Tools/数据库调试/...）
    ///
    /// 背景：加成道具必须在 ItemConfig 表里有对应条目，`BonusItemConfig.itemId` 才能关联上
    /// （`BonusController` 用它反查配置，背包也靠它显示物品）。
    /// 本机没有 sqlite3 命令行、也没有可用的 python，因此借 Unity 内置的
    /// `Mono.Data.Sqlite` 直接读写 Assets/StreamingAssets/game.db。
    ///
    /// 注意：StreamingAssets 里的是「初始库」，运行时首次启动会复制到
    /// persistentDataPath，之后读写的是副本——改初始库后需删除副本才会在本机生效。
    ///
    /// 无头调用：
    ///   Unity.exe -batchmode -nographics -quit -projectPath &lt;工程&gt; -logFile &lt;日志&gt;
    ///             -executeMethod EditorTools.DatabaseTool.RunFromCommandLine
    /// </summary>
    public static class DatabaseTool
    {
        private const string MENU_ROOT = "Tools/数据库调试/";
        private const string DUMP_LOG_FILE = "unity_itemconfig_dump.txt";

        /// <summary>初始库路径（StreamingAssets 下）</summary>
        private static string DbPath => Path.Combine(Application.streamingAssetsPath, "game.db");

        /// <summary>导出结果日志路径（临时目录）</summary>
        private static string DumpLogPath => Path.Combine(Path.GetTempPath(), DUMP_LOG_FILE);

        [MenuItem(MENU_ROOT + "列出物品表", false, 1)]
        public static void DumpItemConfig()
        {
            if (!File.Exists(DbPath))
            {
                Debug.LogError($"[DatabaseTool] 找不到初始库：{DbPath}");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("id\tname\ttype\tmax_stack\tdescription");

            using (SqliteConnection conn = new SqliteConnection("URI=file:" + DbPath))
            {
                conn.Open();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, name, type, description, max_stack FROM ItemConfig ORDER BY id";
                    using (SqliteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sb.AppendLine(string.Join("\t",
                                reader.GetInt32(0).ToString(),
                                reader.GetString(1),
                                reader.GetString(2),
                                reader.GetInt32(4).ToString(),
                                reader.GetString(3)));
                        }
                    }
                }
            }

            File.WriteAllText(DumpLogPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[DatabaseTool] 物品表已导出：{DumpLogPath}\n{sb}");
        }

        // 旧武器归类为加成道具（用户决策）：刀加攻击力，弓和箭加攻速
        private const int ID_IRON_BOW = 1006;       // 铁皮弓 → 近战攻速
        private const int ID_IRON_KNIFE = 1007;     // 铁刀   → 攻击力
        private const int ID_FEATHER_ARROW = 1008;  // 羽毛箭 → 近战攻速
        private const string TYPE_BONUS = "bonus";

        // 早前误建的条目：曾理解为「新建加成道具物品」，实际应改造旧武器而非新增，故清理
        private static readonly int[] StaleBonusIds = { 1009, 1010 };

        /// <summary>
        /// 把背包里的旧武器物品（1006 铁皮弓 / 1007 铁刀 / 1008 羽毛箭）归类为加成道具：
        /// type 改为 bonus、描述改为对应加成效果；并清理误建的 1009 / 1010。
        /// 全部走 SQL 更新，重复执行结果一致（幂等）。
        ///
        /// 说明：真正的武器是 WeaponConfig 资产（锯子/枪），由 EquipmentController.Pickup 装备，
        /// 不占背包格子，因此物品表里不再需要「武器装备」这一分类。
        /// </summary>
        [MenuItem(MENU_ROOT + "旧武器归类为加成道具", false, 2)]
        public static void ReclassifyWeaponsAsBonus()
        {
            if (!File.Exists(DbPath))
            {
                Debug.LogError($"[DatabaseTool] 找不到初始库：{DbPath}");
                return;
            }

            (int Id, string Desc)[] updates =
            {
                (ID_IRON_BOW, "使用后永久提升近战攻速"),
                (ID_IRON_KNIFE, "使用后永久提升攻击力"),
                (ID_FEATHER_ARROW, "使用后永久小幅提升近战攻速"),
            };

            using (SqliteConnection conn = new SqliteConnection("URI=file:" + DbPath))
            {
                conn.Open();

                foreach ((int id, string desc) in updates)
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE ItemConfig SET type = @type, description = @desc WHERE id = @id";
                        cmd.Parameters.AddWithValue("@type", TYPE_BONUS);
                        cmd.Parameters.AddWithValue("@desc", desc);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                foreach (int staleId in StaleBonusIds)
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM ItemConfig WHERE id = @id";
                        cmd.Parameters.AddWithValue("@id", staleId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            Debug.Log($"[DatabaseTool] 已归类为加成道具：{ID_IRON_BOW} 铁皮弓(攻速) / {ID_IRON_KNIFE} 铁刀(攻击力) / " +
                      $"{ID_FEATHER_ARROW} 羽毛箭(攻速)；并清理误建条目 1009、1010");
            WarnRuntimeCopy();
            DumpItemConfig();
        }

        /// <summary>供 -executeMethod 无头调用：归类旧武器后退出 Unity</summary>
        public static void ReclassifyFromCommandLine()
        {
            ReclassifyWeaponsAsBonus();
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 运行时读写的是 persistentDataPath 下的副本，改初始库不会自动同步过去，
        /// 本机实测前需先删除该副本。
        /// </summary>
        private static void WarnRuntimeCopy()
        {
            string runtime = Path.Combine(Application.persistentDataPath, "game.db");
            if (File.Exists(runtime))
            {
                Debug.LogWarning($"[DatabaseTool] 检测到运行时副本：{runtime}\n初始库改动不会自动同步，本机测试前需先删除该副本。");
            }
        }
    }
}
#endif
