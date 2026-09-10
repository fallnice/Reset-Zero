namespace Role.Core
{
    /// <summary>
    /// 瞄准状态读取接口——表达「该角色此刻是否处于瞄准状态」，供相机、UI 准星、动画层等表现方读取。
    ///
    /// 与 IInputProvider.AimHeld 的区别：
    ///   AimHeld  是原始输入（玩家是否按住瞄准键）；
    ///   IsAiming 是角色级聚合结果——需持有远程武器，且（按住瞄准键 或 处于开火后的自动保持期内）。
    /// 两者分离的原因与 IUiInputProvider 一致：原始输入与角色语义各司其职，避免接口污染。
    /// </summary>
    public interface IAimStateProvider
    {
        /// <summary> 当前是否处于瞄准状态；持近战武器、死亡或眩晕时恒为 false </summary>
        bool IsAiming { get; }
    }
}
