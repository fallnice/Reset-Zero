using UnityEngine;
using Core;

namespace Interaction
{
    /// <summary>
    /// 拾取物——实现 IInteractable，交互后通过 IInventory 加入背包
    /// 挂在场景中的道具 GameObject 上，需要 Collider（用于触发检测）
    /// </summary>
    public class PickupItem : MonoBehaviour, IInteractable
    {
        [Header("物品配置")]
        [SerializeField] private int itemId = 1;
        [SerializeField] private int count = 1;
        [SerializeField] private string promptText = "按 E 拾取";

        public bool CanInteract => gameObject.activeInHierarchy;

        public string GetPrompt()
        {
            return $"{promptText} ×{count}";
        }

        public void OnInteract(GameObject interactor)
        {
            // 通过 GameRoot 获取 IInventory 接口，不直接依赖 BagController
            var inventory = GameRoot.Instance != null ? GameRoot.Instance.Inventory : null;
            if (inventory == null)
            {
                Debug.LogError("[PickupItem] GameRoot 或 Inventory 未初始化", this);
                return;
            }

            bool success = inventory.AddItem(itemId, count);
            if (success)
            {
                EventBus.Emit(EventName.Interaction_Performed, this);
                EventBus.Emit(EventName.Bag_Changed);
                Destroy(gameObject);
            }
            else
            {
                const string message = "背包已满，无法拾取";
                Debug.LogWarning($"[PickupItem] {message}", this);
                EventBus.Emit(EventName.UI_Toast, message);
            }
        }
    }
}
