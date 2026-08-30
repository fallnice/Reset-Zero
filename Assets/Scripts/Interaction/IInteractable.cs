using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 可交互对象接口——箱子、拾取物、NPC、门等世界物体实现此接口
    /// 玩家的 InteractionDetector 检测到实现此接口的对象后，按交互键触发 OnInteract
    /// </summary>
    public interface IInteractable
    {
        /// <summary> 当前是否可交互（冷却中/已打开的箱子可返回 false） </summary>
        bool CanInteract { get; }

        /// <summary> 交互提示文本（如 "按 E 拾取"、"按 E 打开"），用于 UI 显示 </summary>
        string GetPrompt();

        /// <summary>
        /// 执行交互
        /// </summary>
        /// <param name="interactor">发起交互的 GameObject（通常是玩家）</param>
        void OnInteract(GameObject interactor);
    }
}
