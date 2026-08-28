using UnityEngine;
using Role.Core;

namespace Role.Controllers
{
    /// <summary>
    /// 装备控制器 Stub——挂载武器模型、更新 Blackboard 中的当前武器信息
    /// </summary>
    public class EquipmentController : MonoBehaviour, IStateResponder
    {
        public void OnStateEnter(CharacterState state)
        {
            // TODO: 根据状态切换武器姿态（收起/拔出）
        }

        public void OnStateExit(CharacterState state) { }
    }
}
