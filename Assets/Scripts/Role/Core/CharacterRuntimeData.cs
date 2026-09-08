namespace Role.Core
{
    /// <summary>
    /// 角色运行时数据——实例级容器，替代静态 Blackboard 中与单个角色绑定的瞬时状态。
    /// 每个 CharacterRoot 持有一份，多角色（玩家/敌人）之间互不干扰。
    /// </summary>
    public class CharacterRuntimeData
    {
        /// <summary> 当前水平移动速度（Idle=0，Walk/Run 各自速度；空中继承此值保持水平速度） </summary>
        public float moveSpeed;

        /// <summary> 空中垂直速度（Jump 结束时传给 Fall 继承，保证速度连续） </summary>
        public float airVerticalVelocity;
    }
}
