using System.Text;
using Core;
using Model;
using TMPro;
using UnityEngine;

namespace View
{
    /// <summary> 渲染当前单任务标题、状态与目标进度。 </summary>
    public sealed class QuestView : MonoBehaviour
    {
        [SerializeField] private TMP_Text questText;

        private readonly StringBuilder _builder = new StringBuilder(256);
        private EventBus.SubscriptionToken _questChangedToken;
        private bool _hasWarnedMissingText;

        private void Awake()
        {
            if (questText == null && !TryGetComponent(out questText) && !_hasWarnedMissingText)
            {
                Debug.LogWarning("[QuestView] 未配置 TMP_Text，任务 HUD 无法渲染", this);
                _hasWarnedMissingText = true;
            }
        }

        private void OnEnable()
        {
            _questChangedToken = EventBus.Subscribe(EventName.Quest_Changed, HandleQuestChanged);
            EventBus.Emit(EventName.Quest_Query);
        }

        private void OnDisable()
        {
            _questChangedToken?.Dispose();
            _questChangedToken = null;
        }

        /// <summary> 根据任务状态和进度事件刷新文本。 </summary>
        private void HandleQuestChanged(object[] args)
        {
            if (questText == null || args == null || args.Length < 3
                || !(args[0] is QuestDefinition definition)
                || !(args[1] is QuestState state) || !(args[2] is int[] progress))
                return;

            if (state == QuestState.NotAccepted || state == QuestState.Completed)
            {
                questText.text = string.Empty;
                return;
            }

            _builder.Clear();
            _builder.Append(definition.Title);
            if (state == QuestState.ReadyToTurnIn)
                _builder.Append("（可交付）");

            for (int i = 0; i < definition.Objectives.Count; i++)
            {
                QuestObjective objective = definition.Objectives[i];
                int current = i < progress.Length ? progress[i] : 0;
                _builder.Append('\n').Append(objective.Description)
                    .Append(' ').Append(current).Append('/').Append(objective.RequiredCount);
            }
            questText.text = _builder.ToString();
        }
    }
}
