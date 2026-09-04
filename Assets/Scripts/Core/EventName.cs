namespace Core
{
    /// <summary>
    /// 全局事件名常量 — 避免字符串硬编码导致的拼写错误
    /// 
    /// 命名规范：系统名_动作（全部小写+下划线）
    /// 使用方式：EventBus.Emit(EventName.Bag_ItemAdded, itemId, count);
    /// </summary>
    public static class EventName
    {
        // ===== 背包系统 =====
        public const string Bag_ItemAdded    = "bag_item_added";
        public const string Bag_ItemRemoved  = "bag_item_removed";
        public const string Bag_Changed      = "bag_changed";       // 背包任意变动后刷新 UI

        // ===== 制作系统 =====
        public const string Craft_Success    = "craft_success";
        public const string Craft_Fail       = "craft_fail";

        // ===== UI 面板 =====
        public const string UI_BagOpened     = "ui_bag_opened";
        public const string UI_BagClosed     = "ui_bag_closed";
        public const string UI_CraftOpened   = "ui_craft_opened";
        public const string UI_CraftClosed   = "ui_craft_closed";
        public const string UI_AllClosed     = "ui_all_closed";
        public const string UI_ForceClose_Bag = "ui_force_close_bag";  // 角色死亡/过场时强制关闭背包
        public const string UI_Toast         = "ui_toast";            // (string message) 全局浮动提示
        public const string UI_ModalChanged  = "ui_modal_changed";    // (bool isModalOpen) 任一模态面板打开/关闭

        // ===== 角色系统 =====
        public const string Character_StateChanged = "character_state_changed"; // (oldState, newState)
        public const string Character_Died        = "character_died";
        public const string Character_Respawned  = "character_respawned";

        // ===== 交互系统 =====
        public const string Interaction_TargetChanged = "interaction_target_changed"; // (string prompt) 目标变化，null 表示无目标
        public const string Interaction_Performed     = "interaction_performed";      // (IInteractable target) 交互触发

        // ===== 武器/装备系统 =====
        public const string Weapon_Equipped = "weapon_equipped"; // (oldWeapon, newWeapon) 武器切换完成
        public const string Weapon_Dropped  = "weapon_dropped";  // (weapon) 丢弃当前武器

        // ===== 加成道具 =====
        public const string Bonus_Used = "bonus_used"; // (itemId) 加成道具使用成功
    }
}
