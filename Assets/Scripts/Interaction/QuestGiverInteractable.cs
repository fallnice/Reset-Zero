using Controller;
using UnityEngine;

namespace Interaction
{
    /// <summary> 把任务接取与交付入口适配到统一交互系统。 </summary>
    public sealed class QuestGiverInteractable : MonoBehaviour, IInteractable
    {
        private QuestController _questController;
        private bool _hasWarnedMissingController;

        /// <summary> 任务未完成后仍允许查看提示，完成后关闭交互。 </summary>
        public bool CanInteract => isActiveAndEnabled && _questController != null
            && (_questController.State == Model.QuestState.NotAccepted
                || _questController.State == Model.QuestState.ReadyToTurnIn);

        /// <summary> 注入任务控制器；由 GameRoot 在数据库和任务系统就绪后调用。 </summary>
        public void Initialize(QuestController questController)
        {
            if (questController == null)
            {
                Debug.LogWarning("[QuestGiverInteractable] 注入的 QuestController 为空", this);
                return;
            }
            _questController = questController;
        }

        /// <summary> 返回当前任务状态对应的交互提示。 </summary>
        public string GetPrompt()
        {
            return _questController != null ? _questController.GetInteractionPrompt() : "任务暂不可用";
        }

        /// <summary> 根据任务状态执行接取或交付。 </summary>
        public void OnInteract(GameObject interactor)
        {
            if (interactor == null)
            {
                Debug.LogWarning("[QuestGiverInteractable] 交互者为空", this);
                return;
            }
            if (_questController == null)
            {
                if (!_hasWarnedMissingController)
                {
                    Debug.LogWarning("[QuestGiverInteractable] QuestController 尚未注入", this);
                    _hasWarnedMissingController = true;
                }
                return;
            }

            _questController.TryInteract();
        }
    }
}
