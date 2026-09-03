using UnityEngine;
using Combat;
using Core;
using Role;
using Role.Controllers;

namespace Interaction
{
    /// <summary>
    /// 武器掉落物——实现 IInteractable，交互后进专属武器栏（不进背包）
    /// 与 PickupItem（itemId 进背包）的区别：这里拾取 WeaponConfig，落入固定三槽。
    /// 目标槽位已有武器时拒绝拾取并提示，不会覆盖旧武器。
    /// 挂场景武器 GameObject 上，需要 Collider（用于交互检测）。
    /// </summary>
    public class WeaponPickupItem : MonoBehaviour, IInteractable
    {
        [Header("武器配置")]
        [SerializeField] private WeaponConfig weaponConfig;

        [Header("提示文本")]
        [SerializeField] private string promptText = "按 E 拾取";

        public bool CanInteract => gameObject.activeInHierarchy && weaponConfig != null;

        public string GetPrompt()
        {
            string weaponName = weaponConfig != null ? weaponConfig.weaponName : "武器";
            return $"{promptText} {weaponName}";
        }

        public void OnInteract(GameObject interactor)
        {
            if (weaponConfig == null)
            {
                Debug.LogError("[WeaponPickupItem] weaponConfig 未赋值，无法拾取", this);
                return;
            }

            EquipmentController equipment = ResolveEquipment(interactor);
            if (equipment == null)
            {
                Debug.LogError("[WeaponPickupItem] 未找到角色的 EquipmentController，无法拾取", this);
                return;
            }

            if (!equipment.TryPickup(weaponConfig, out string failReason))
            {
                Debug.LogWarning($"[WeaponPickupItem] 拾取失败：{failReason}", this);
                EventBus.Emit(EventName.UI_Toast, failReason);
                return;
            }

            EventBus.Emit(EventName.Interaction_Performed, this);
            Destroy(gameObject);
        }

        /// <summary> 从交互者解析装备控制器（优先角色根暴露的引用，缺失时从子级兜底查找） </summary>
        private static EquipmentController ResolveEquipment(GameObject interactor)
        {
            if (interactor == null) return null;

            CharacterRoot root = interactor.GetComponentInParent<CharacterRoot>();
            if (root != null && root.Equipment != null)
                return root.Equipment;

            return interactor.GetComponentInChildren<EquipmentController>();
        }
    }
}
