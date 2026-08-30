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

        [Header("鼠标旋转")]
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float minPitch = -20f;   // 最低俯角
        [SerializeField] private float maxPitch = 60f;    // 最高仰角

        [Header("平滑")]
        [SerializeField] private float smoothTime = 0.12f;

        private float _yaw;     // 水平旋转角
        private float _pitch;   // 垂直俯仰角
        private Vector3 _smoothVelocity;

        private void Start()
        {
            _yaw = target != null ? target.eulerAngles.y : 0f;
            _pitch = 15f;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 鼠标移动控制旋转
            _yaw   += UnityEngine.Input.GetAxis("Mouse X") * rotationSpeed;
            _pitch -= UnityEngine.Input.GetAxis("Mouse Y") * rotationSpeed;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // 计算相机目标位置（球面坐标）
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetPos = target.position + Vector3.up * height;
            Vector3 desiredPos = targetPos - (rotation * Vector3.forward * distance);

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
