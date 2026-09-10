using UnityEngine;

namespace Role
{
    /// <summary>
    /// 第三人称相机——鼠标控制旋转，平滑跟随，上下角度有范围限制
    /// 挂在 Main Camera 上，Target 拖角色
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("目标")]
        [SerializeField] private Transform target;

        [Header("距离 & 高度")]
        [SerializeField] private float distance = 5f;
        [SerializeField] private float height = 2f;

        [Header("瞄准拉近")]
        [Tooltip("进入瞄准状态时的相机距离（右键按住，或远程开火后的自动保持期）；普通距离见上方 distance")]
        [SerializeField] private float aimDistance = 2.5f;
        [Tooltip("普通 ↔ 瞄准 之间过渡的平滑时间（秒）")]
        [SerializeField] private float aimSmoothTime = 0.15f;

        [Header("鼠标旋转")]
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float minPitch = -20f;   // 最低俯角
        [SerializeField] private float maxPitch = 60f;    // 最高仰角

        [Header("平滑")]
        [SerializeField] private float smoothTime = 0.12f;

        private float _yaw;     // 水平旋转角
        private float _pitch;   // 垂直俯仰角
        private Vector3 _smoothVelocity;

        private float _currentDistance;     // 当前实际距离（在 distance 与 aimDistance 之间平滑过渡）
        private float _distanceVelocity;    // 距离平滑速度（SmoothDamp 内部状态）

        // 输入提供者——跨 MonoBehaviour 引用不在 Awake 缓存，LateUpdate 中延迟获取
        private Role.Core.IInputProvider _inputProvider;
        private bool _inputMissingWarned;   // 同类 Warning 只打印一次

        // 瞄准状态——角色级聚合结果（输入 + 武器类型 + 开火自动进入），同样延迟获取
        private Role.Core.IAimStateProvider _aimStateProvider;

        private void Start()
        {
            _yaw = target != null ? target.eulerAngles.y : 0f;
            _pitch = 15f;
            _currentDistance = distance;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 延迟获取输入提供者（首次非 null 后缓存）
            if (_inputProvider == null)
            {
                _inputProvider = target.GetComponentInChildren<Role.Core.IInputProvider>();
                if (_inputProvider == null)
                {
                    if (!_inputMissingWarned)
                    {
                        _inputMissingWarned = true;
                        Debug.LogWarning("[CameraFollow] 未在角色身上找到 IInputProvider，相机无法旋转");
                    }
                    return;
                }
            }

            // 延迟获取瞄准状态（首次非 null 后缓存；角色未实现该接口时一律视为未瞄准）
            if (_aimStateProvider == null)
                _aimStateProvider = target.GetComponentInChildren<Role.Core.IAimStateProvider>();

            // 鼠标/右摇杆输入控制旋转（统一走 Input System 的 Look action）
            UnityEngine.Vector2 lookDelta = _inputProvider.LookDelta;
            _yaw   += lookDelta.x * rotationSpeed;
            _pitch -= lookDelta.y * rotationSpeed;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // 瞄准时拉近：距离在普通值与瞄准值之间平滑过渡，避免进入/退出瞄准瞬间跳变
            bool isAiming = _aimStateProvider != null && _aimStateProvider.IsAiming;
            float targetDistance = isAiming ? aimDistance : distance;
            _currentDistance = Mathf.SmoothDamp(
                _currentDistance, targetDistance, ref _distanceVelocity, aimSmoothTime);

            // 计算相机目标位置（球面坐标）
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetPos = target.position + Vector3.up * height;
            Vector3 desiredPos = targetPos - (rotation * Vector3.forward * _currentDistance);

            // 平滑移动
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _smoothVelocity, smoothTime);
            transform.LookAt(targetPos);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        /// <summary> 外部获取当前相机水平朝向（角色移动方向用） </summary>
        public float Yaw => _yaw;
    }
}
