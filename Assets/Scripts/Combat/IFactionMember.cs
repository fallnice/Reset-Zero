namespace Combat
{
    /// <summary>
    /// 阵营成员接口——拥有阵营身份的对象（角色、召唤物、可破坏物）实现，
    /// 供武器行为在构造伤害上下文时解析攻击方阵营。
    /// </summary>
    public interface IFactionMember
    {
        Faction Faction { get; }
    }
}
