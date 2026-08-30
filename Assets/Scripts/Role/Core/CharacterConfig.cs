using UnityEngine;

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
    }
}
