using System.Collections;
using Core;
using UnityEngine;
using UnityEngine.UI;

namespace View
{
    /// <summary>
    /// 全局浮动提示——挂在常驻 Canvas 下的 Text 物体上。
    /// 任意模块通过 EventBus.Emit(EventName.UI_Toast, message) 触发；
    /// 新消息会中断上一条并从初始位置重新淡入、上飘、淡出。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class ToastView : MonoBehaviour
    {
        [Header("动画")]
        [SerializeField, Min(0.1f)] private float duration = 2f;
        [SerializeField, Min(0f)] private float floatDistance = 60f;
        [SerializeField, Range(0.01f, 0.99f)] private float fadeInRatio = 0.15f;

        private Text _text;
        private RectTransform _rectTransform;
        private Vector2 _startPosition;
        private Color _baseColor;
        private Coroutine _animation;
        private EventBus.SubscriptionToken _toastToken;

        private void Awake()
        {
            _text = GetComponent<Text>();
            _rectTransform = _text.rectTransform;
            _startPosition = _rectTransform.anchoredPosition;
            _baseColor = _text.color;
            _text.raycastTarget = false;
            HideImmediately();
        }

        private void OnEnable()
        {
            _toastToken = EventBus.Subscribe(EventName.UI_Toast, OnToastRequested);
        }

        private void OnDisable()
        {
            _toastToken?.Dispose();
            _toastToken = null;

            if (_animation != null)
            {
                StopCoroutine(_animation);
                _animation = null;
            }

            if (_text != null)
                HideImmediately();
        }

        private void OnToastRequested(object[] args)
        {
            if (args == null || args.Length == 0 || !(args[0] is string message) || string.IsNullOrWhiteSpace(message))
                return;

            if (_animation != null)
                StopCoroutine(_animation);

            _animation = StartCoroutine(PlayToast(message));
        }

        private IEnumerator PlayToast(string message)
        {
            _text.text = message;
            _rectTransform.anchoredPosition = _startPosition;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float alpha = progress < fadeInRatio
                    ? progress / fadeInRatio
                    : 1f - (progress - fadeInRatio) / (1f - fadeInRatio);

                SetAlpha(alpha);
                _rectTransform.anchoredPosition = _startPosition + Vector2.up * (floatDistance * progress);
                yield return null;
            }

            HideImmediately();
            _animation = null;
        }

        private void HideImmediately()
        {
            _text.text = string.Empty;
            _rectTransform.anchoredPosition = _startPosition;
            SetAlpha(0f);
        }

        private void SetAlpha(float alpha)
        {
            Color color = _baseColor;
            color.a = Mathf.Clamp01(alpha);
            _text.color = color;
        }
    }
}
