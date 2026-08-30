namespace Role.Core
{
    /// <summary>
    /// UI 面板输入接口——玩家专属输入（AI 无需实现）
    /// 与 IInputProvider 分离：角色移动输入与 UI 快捷键各司其职，避免接口污染
    /// </summary>
    public interface IUiInputProvider
    {
        /// <summary> 本帧是否按下背包开关（I 键，边缘触发） </summary>
        bool OpenBagPressed { get; }

        /// <summary> 本帧是否按下制造开关（C 键，边缘触发） </summary>
        bool OpenCraftPressed { get; }

        /// <summary> 本帧是否按下背包/制造互斥切换（Tab 键，边缘触发） </summary>
        bool TogglePanelPressed { get; }

        /// <summary> 本帧是否按下关闭全部面板（Esc 键，边缘触发） </summary>
        bool CloseAllPressed { get; }
    }
}
