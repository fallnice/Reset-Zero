using System;
using UnityEngine;

namespace Model
{
    /// <summary> 描述一个可持久化计数的任务目标。 </summary>
    [Serializable]
    public sealed class QuestObjective
    {
        [SerializeField] private QuestObjectiveType type;
        [Min(0)] [SerializeField] private int targetId;
        [Min(1)] [SerializeField] private int requiredCount = 1;
        [SerializeField] private string description;

        /// <summary> 目标类型。 </summary>
        public QuestObjectiveType Type => type;

        /// <summary> 目标 ID；击杀目标为 0 时表示任意敌人。 </summary>
        public int TargetId => targetId;

        /// <summary> 完成目标需要的累计数量。 </summary>
        public int RequiredCount => requiredCount;

        /// <summary> 面向玩家的目标说明。 </summary>
        public string Description => description;

        /// <summary> 当前目标配置是否可用于运行时统计。 </summary>
        public bool IsValid => requiredCount > 0
            && (type == QuestObjectiveType.Kill ? targetId == 0 : targetId > 0);
    }
}
