using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Model
{
    /// <summary> 配置单条任务的目标、文本与交付奖励。 </summary>
    [CreateAssetMenu(fileName = "QuestDefinition", menuName = "Quest/Quest Definition")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [SerializeField] private string questId = "first_quest";
        [Min(1)] [SerializeField] private int definitionVersion = 1;
        [SerializeField] private string title = "第一项委托";
        [TextArea] [SerializeField] private string description;
        [SerializeField] private List<QuestObjective> objectives = new List<QuestObjective>();
        [Min(1)] [SerializeField] private int rewardItemId = 1;
        [Min(1)] [SerializeField] private int rewardCount = 1;

        private ReadOnlyCollection<QuestObjective> _readOnlyObjectives;

        /// <summary> 用于存档主键的稳定任务 ID。 </summary>
        public string QuestId => questId;

        /// <summary> 目标结构或语义发生不兼容变化时递增的定义版本。 </summary>
        public int DefinitionVersion => definitionVersion;

        /// <summary> 任务标题。 </summary>
        public string Title => title;

        /// <summary> 任务描述。 </summary>
        public string Description => description;

        /// <summary> 只读目标列表。 </summary>
        public IReadOnlyList<QuestObjective> Objectives
        {
            get
            {
                if (objectives == null)
                    objectives = new List<QuestObjective>();
                if (_readOnlyObjectives == null)
                    _readOnlyObjectives = objectives.AsReadOnly();
                return _readOnlyObjectives;
            }
        }

        /// <summary> 交付后发放的物品 ID。 </summary>
        public int RewardItemId => rewardItemId;

        /// <summary> 交付后发放的物品数量。 </summary>
        public int RewardCount => rewardCount;

        /// <summary> 当前任务定义是否满足首版运行条件。 </summary>
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(questId) || string.IsNullOrWhiteSpace(title)
                || definitionVersion <= 0 || rewardItemId <= 0 || rewardCount <= 0
                || objectives == null || objectives.Count == 0)
                return false;

            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i] == null || !objectives[i].IsValid) return false;
            }
            return true;
        }

        private void OnValidate()
        {
            _readOnlyObjectives = null;
            definitionVersion = Mathf.Max(1, definitionVersion);
            rewardItemId = Mathf.Max(1, rewardItemId);
            rewardCount = Mathf.Max(1, rewardCount);
        }
    }
}
