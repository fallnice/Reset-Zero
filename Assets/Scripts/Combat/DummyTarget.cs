using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 训练假人——测试目标的「死亡策略」薄封装，实际生命逻辑由 HealthController 承担。
    /// 挂在本对象时自动补上 HealthController（实现 IDamageable）与 Collider。
    /// </summary>
    [RequireComponent(typeof(HealthController))]
    public class DummyTarget : MonoBehaviour
    {
        [Header("死亡表现")]
        [Tooltip("血量归零后是否销毁自身；否则仅停止受击（可 ResetHealth 复活）")]
        public bool destroyOnDeath = false;

        private HealthController _health;

        private void Awake()
        {
            _health = GetComponent<HealthController>();
            if (_health != null)
                _health.Died += HandleDied;
            EnsureCollider();
        }

        private void OnDestroy()
        {
            if (_health != null)
                _health.Died -= HandleDied;
        }

        private void HandleDied()
        {
            if (destroyOnDeath)
                Destroy(gameObject);
        }

        /// <summary> 没有碰撞体就无法被 OverlapSphere / Raycast 命中，自动补一个保证开箱可用 </summary>
        private void EnsureCollider()
        {
            if (GetComponent<Collider>() == null)
                gameObject.AddComponent<SphereCollider>();
        }
    }
}
