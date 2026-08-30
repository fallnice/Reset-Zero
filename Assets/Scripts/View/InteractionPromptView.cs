using UnityEngine;
using UnityEngine.UI;
using Core;

namespace View
{
    /// <summary>
    /// 交互提示UI——直接挂在 Text 物体上
    /// 订阅 EventBus.Interaction_TargetChanged，收到非空文本时显示，否则隐藏
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class InteractionPromptView : MonoBehaviour
    {
        private Text _text;
        private EventBus.SubscriptionToken _token;

        private void Awake()
        {
            _text = GetComponent<Text>();
            _text.text = "";
        }

        private void OnEnable()
        {
            _token = EventBus.Subscribe(EventName.Interaction_TargetChanged, OnTargetChanged);
        }

        private void OnDisable()
        {
            _token?.Dispose();
        }

        private void OnTargetChanged(object[] args)
        {
            if (args == null || args.Length == 0 || args[0] == null)
            {
                _text.text = "";
                return;
            }

            string prompt = args[0] as string;
            _text.text = string.IsNullOrEmpty(prompt) ? "" : prompt;
        }
    }
}
