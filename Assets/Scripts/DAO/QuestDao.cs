using System;
using System.Globalization;
using System.Text;
using Core;
using Model;
using Mono.Data.Sqlite;

namespace DAO
{
    /// <summary> 负责创建任务存档表并读写单任务进度。 </summary>
    public sealed class QuestDao : IQuestRepository
    {
        /// <summary> 确保运行时数据库包含任务进度表。 </summary>
        public void EnsureTable()
        {
            RequireDatabase();
            SqliteManager.Instance.ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS QuestProgress ("
                + "QuestId TEXT PRIMARY KEY NOT NULL, "
                + "DefinitionVersion INTEGER NOT NULL DEFAULT 1, "
                + "State INTEGER NOT NULL, "
                + "Progress TEXT NOT NULL)");
            try
            {
                SqliteManager.Instance.ExecuteNonQuery(
                    "ALTER TABLE QuestProgress ADD COLUMN DefinitionVersion INTEGER NOT NULL DEFAULT 1");
            }
            catch (SqliteException exception)
            {
                if (!exception.Message.Contains("duplicate column name")) throw;
            }
        }

        /// <summary> 读取指定任务存档；没有记录时返回 null。 </summary>
        public QuestProgressData Load(string questId)
        {
            ValidateQuestId(questId);
            RequireDatabase();

            string escapedId = Escape(questId);
            using SqliteDataReader reader = SqliteManager.Instance.ExecuteQuery(
                "SELECT DefinitionVersion, State, Progress FROM QuestProgress "
                + $"WHERE QuestId='{escapedId}' LIMIT 1");
            if (!reader.Read()) return null;

            return new QuestProgressData
            {
                DefinitionVersion = Convert.ToInt32(
                    reader["DefinitionVersion"], CultureInfo.InvariantCulture),
                State = (QuestState)Convert.ToInt32(reader["State"], CultureInfo.InvariantCulture),
                Progress = ParseProgress(Convert.ToString(reader["Progress"], CultureInfo.InvariantCulture))
            };
        }

        /// <summary> 原子写入任务状态和全部目标进度。 </summary>
        public void Save(string questId, int definitionVersion, QuestState state, int[] progress)
        {
            ValidateQuestId(questId);
            if (definitionVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            RequireDatabase();

            string escapedId = Escape(questId);
            string escapedProgress = Escape(SerializeProgress(progress));
            int stateValue = (int)state;
            SqliteManager.Instance.ExecuteNonQuery(
                "INSERT INTO QuestProgress (QuestId, DefinitionVersion, State, Progress) VALUES ("
                + $"'{escapedId}', {definitionVersion}, {stateValue}, '{escapedProgress}') "
                + "ON CONFLICT(QuestId) DO UPDATE SET DefinitionVersion=excluded.DefinitionVersion, "
                + "State=excluded.State, Progress=excluded.Progress");
        }

        /// <summary> 把目标进度编码为稳定的逗号分隔文本。 </summary>
        private static string SerializeProgress(int[] progress)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < progress.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(Math.Max(0, progress[i]).ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary> 解析数据库中的目标进度文本。 </summary>
        private static int[] ParseProgress(string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return Array.Empty<int>();
            string[] values = serialized.Split(',');
            var result = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (!int.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    value = 0;
                result[i] = Math.Max(0, value);
            }
            return result;
        }

        /// <summary> 转义 SQLite 文本字面量中的单引号。 </summary>
        private static string Escape(string value)
        {
            return value.Replace("'", "''");
        }

        /// <summary> 校验外部任务 ID。 </summary>
        private static void ValidateQuestId(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                throw new ArgumentException("任务 ID 不能为空", nameof(questId));
        }

        /// <summary> 确认数据库已经初始化。 </summary>
        private static void RequireDatabase()
        {
            if (SqliteManager.Instance == null || !SqliteManager.Instance.IsReady)
                throw new InvalidOperationException("数据库未初始化，无法访问任务存档");
        }
    }
}
