using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 直线导航——朝目标水平方向移动，前方有障碍时横向偏转绕过。
    /// 垂直切片的最简实现；后续替换为 IEnemyNavigation + A*（补路径重算节流与卡住诊断）。
    /// </summary>
    public class DirectNavigation
    {
        private readonly EnemyConfig _config;

        public DirectNavigation(EnemyConfig config)
        {
            _config = config;
        }

        /// <summary> 计算本帧移动方向（世界空间，已归一化） </summary>
        public Vector3 ComputeMoveDirection(Transform self, Vector3 targetPosition)
        {
            Vector3 toTarget = targetPosition - self.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 dir = toTarget.normalized;
            if (_config == null) return dir;

            // 前方障碍检测：从胸口高度向前打射线；命中障碍则向右侧偏转绕过。
            // 攻击距离（约 2m）大于检测距离（约 1.5m），追击阶段不会把目标自身当成障碍。
            Vector3 origin = self.position + Vector3.up * 0.6f;
            if (Physics.Raycast(origin, dir, out RaycastHit _, _config.obstacleAvoidDistance))
            {
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                dir = (dir + right * 0.8f).normalized;
            }

            return dir;
        }
    }
}
