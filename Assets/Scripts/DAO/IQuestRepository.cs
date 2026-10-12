using Model;

namespace DAO
{
    /// <summary> 抽象任务存档读写，便于自动验收和未来服务端持久化替换。 </summary>
    public interface IQuestRepository
    {
        /// <summary> 确保任务存档结构可用。 </summary>
        void EnsureTable();

        /// <summary> 读取指定任务存档；没有记录时返回 null。 </summary>
        QuestProgressData Load(string questId);

        /// <summary> 保存任务定义版本、状态与全部目标进度。 </summary>
        void Save(string questId, int definitionVersion, QuestState state, int[] progress);
    }
}
