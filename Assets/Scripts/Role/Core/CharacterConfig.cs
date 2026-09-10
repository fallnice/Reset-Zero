using UnityEngine;
using Combat;

namespace Role.Core
{
    /// <summary>
    /// 角色配置——ScriptableObject，纯数据容器
    /// 在 Unity 中通过 Create > Role > Character Config 创建资产
    /// 挂到 CharacterRoot 的 Config 字段上，不同角色可复用 / 覆盖
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "Role/Character Config", order = 0)]
    public class CharacterConfig : ScriptableObject
    {
        [Header("移动速度")]
        [Min(0f)] public float walkSpeed = 3.5f;
        [Min(0f)] public float runSpeed = 7f;

        [Header("跳跃 & 重力")]
        [Min(0f)] public float jumpForce = 8f;
        public float gravity = -20f;
        public float groundedStickForce = -2f;   // 贴地时施加的下压力，保持 isGrounded 稳定

        [Header("转身")]
        [Min(0.1f)] public float rotationSpeed = 12f;

        [Header("瞄准")]
        [Tooltip("开火后自动进入瞄准状态的保持时长（秒）；按住瞄准键期间不受此值影响，可无限持续")]
        [Min(0f)] public float aimAutoHoldSeconds = 1.5f;

        [Tooltip("瞄准时角色转向相机方向的角速度系数；需明显高于 rotationSpeed 才跟手（调大更跟手）")]
        [Min(0.1f)] public float aimRotationSpeed = 30f;

        [Header("初始装备")]
        [Tooltip("开局自动装备的武器，各按自己的 slot 落槽；数组最后一个成为手持武器。留空则出生空手")]
        public WeaponConfig[] initialWeapons;
    }
}
