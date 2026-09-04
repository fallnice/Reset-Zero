namespace Core
{
    /// <summary>
    /// 模态面板标记接口——实现此接口的面板打开时会进入「UI 模态」，
    /// 由 UIManager 统一广播 UI_ModalChanged，角色据此阻断战斗输入（攻击/切枪/丢弃）。
    /// 非模态面板（HUD/Toast 等）不实现此接口，不影响战斗输入。
    /// </summary>
    public interface IModalPanel
    {
    }
}
