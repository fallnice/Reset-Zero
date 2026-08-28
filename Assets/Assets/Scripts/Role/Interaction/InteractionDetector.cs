using UnityEngine;
using Role.Core;
using Core;
using Interaction;

namespace Role.Interaction
{
    /// <summary>
    /// 交互检测器——挂在玩家子物体上，定时用 OverlapSphere 检测附近的 IInteractable
    /// 选出最近的可交互对象，按交互键（E）触发 OnInteract
    /// 通过 EventBus 广播当前目标变化，UI 订阅后显示提示文本
    ///
    /// 性能设计：
    /// 1. LayerMask 过滤——只检测可交互层
    /// 2. 降频检测——0.1秒一次，交互不需要每帧精度
    /// 3. OverlapSphereNonAlloc——预分配数组，零 GC
    /// 4. 只在目标变化时才 Emit 事件，不变化不广播
    /// </summary>
    public class InteractionDetector : MonoBehaviour
    {
        [Header("检测范围")]
        [SerializeField] private float detectRadius = 2f;

        [Header("性能优化")]
        [Tooltip("只检测这些层的物体。留空 = 检测所有层")]
        [SerializeField] private LayerMask interactableMask = 0;

        [Tooltip("检测间隔（秒），不需要每帧检测")]
        [SerializeField] private float detectInterval = 0.1f;

        private readonly Collider[] _hits = new Collider[16];
        private IInteractable _currentTarget;
        private CharacterRoot _root;
        private IInputProvider _input;
        private float _timer;
        private bool _inputMissingWarned;

        private void Awake()
        {
            _root = GetComponentInParent<CharacterRoot>();
            if (_root == null)
                Debug.LogError("[InteractionDetector] 未找到 CharacterRoot！", this);
        }

        private void Update()
        {
            // 延迟获取 inputProvider（Awake 顺序不保证）
            if (_input == null)
            {
                _input = _root?.inputProvider;
                if (_input == null)
                {
                    // 只警告一次，不每帧刷屏
                    if (!_inputMissingWarned)
                    {
                        _inputMissingWarned = true;
                        Debug.LogWarning("[InteractionDetector] inputProvider 为空，请确认角色上挂了 PlayerInputProvider", this);
                    }
                    return;
                }
            }

            _timer += Time.deltaTime;
            if (_timer >= detectInterval)
            {
                _timer = 0f;
                DetectBestTarget();
            }

            if (_input.InteractPressed && _currentTarget != null)
            {
                _currentTarget.OnInteract(_root != null ? _root.gameObject : gameObject);
            }
        }

        private void DetectBestTarget()
        {
            Vector3 selfPos = transform.position;

            int count = interactableMask != 0
                ? Physics.OverlapSphereNonAlloc(selfPos, detectRadius, _hits, interactableMask)
                : Physics.OverlapSphereNonAlloc(selfPos, detectRadius, _hits);

            IInteractable best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;

                var interactable = _hits[i].GetComponent<IInteractable>();
                if (interactable == null || !interactable.CanInteract) continue;

                var mb = interactable as MonoBehaviour;
                if (mb == null) continue;

                float sqr = (mb.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = interactable;
                }
            }

            // 只在目标变化时才广播
            if (best != _currentTarget)
            {
                _currentTarget = best;
                string prompt = best != null ? best.GetPrompt() : null;
                EventBus.Emit(EventName.Interaction_TargetChanged, prompt);
            }
        }

        private void OnDisable()
        {
            if (_currentTarget != null)
            {
                _currentTarget = null;
                EventBus.Emit(EventName.Interaction_TargetChanged, null);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, detectRadius);
        }
#endif
    }
}
