namespace Model
{
    /// <summary> 从数据库读取的单任务状态与目标进度。 </summary>
    public sealed class QuestProgressData
    {
        /// <summary> 存档对应的任务定义版本。 </summary>
        public int DefinitionVersion { get; set; }

        /// <summary> 已持久化的任务状态。 </summary>
        public QuestState State { get; set; }

        /// <summary> 与任务定义目标顺序一致的累计进度。 </summary>
        public int[] Progress { get; set; }
    }
}
