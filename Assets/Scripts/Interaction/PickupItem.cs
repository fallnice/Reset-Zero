using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 场景物品拾取交互；通过 IInventory 写入背包，成功后发布交互和拾取领域事件。
    /// </summary>
    public class PickupItem : MonoBehaviour, IInteractable
    {
        [Header("物品配置")]
        [SerializeField] private int itemId = 1;
        [SerializeField] private int count = 1;
        [SerializeField] private string promptText = "按 E 拾取";

        /// <summary> 物体启用时允许交互。 </summary>
        public bool CanInteract => gameObject.activeInHierarchy;

        /// <summary> 返回当前拾取物的交互提示。 </summary>
        public string GetPrompt()
        {
            return $"{promptText} ×{count}";
        }

        /// <summary> 尝试把配置数量写入背包；失败时保留场景物体。 </summary>
        public void OnInteract(GameObject interactor)
        {
            if (interactor == null)
            {
                Debug.LogWarning("[PickupItem] 交互者为空，已取消拾取", this);
                return;
            }
            if (itemId <= 0 || count <= 0)
            {
                Debug.LogWarning($"[PickupItem] 物品配置非法 ID:{itemId} 数量:{count}", this);
                return;
            }

            // 通过 GameRoot 获取抽象库存，避免拾取物直接依赖 BagController。
            var inventory = GameRoot.Instance != null ? GameRoot.Instance.Inventory : null;
            if (inventory == null)
            {
                Debug.LogWarning("[PickupItem] GameRoot 或 Inventory 未初始化", this);
                return;
            }

            if (inventory.TryAddItem(itemId, count, out InventoryOperationResult result))
            {
                EventBus.Emit(EventName.Interaction_Performed, this);
                EventBus.Emit(EventName.Pickup_Completed, itemId, count);
                Destroy(gameObject);
                return;
            }

            string message = GetFailureMessage(result);
            Debug.LogWarning($"[PickupItem] {message}", this);
            EventBus.Emit(EventName.UI_Toast, message);
        }

        /// <summary> 将库存失败类型转换为面向玩家的简短提示。 </summary>
        private static string GetFailureMessage(InventoryOperationResult result)
        {
            switch (result)
            {
                case InventoryOperationResult.InsufficientSpace:
                    return "背包已满，无法拾取";
                case InventoryOperationResult.ItemNotFound:
                    return "物品配置不存在，无法拾取";
                case InventoryOperationResult.PersistenceFailed:
                    return "保存背包失败，请稍后重试";
                case InventoryOperationResult.NotInitialized:
                    return "背包尚未初始化";
                default:
                    return "拾取失败";
            }
        }

        /// <summary> 在编辑器中约束物品配置，避免无效拾取物进入场景。 </summary>
        private void OnValidate()
        {
            itemId = Mathf.Max(1, itemId);
            count = Mathf.Max(1, count);
        }
    }
}
