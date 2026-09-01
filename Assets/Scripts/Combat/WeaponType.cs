namespace Combat
{
    /// <summary>
    /// 武器类型——决定攻击行为策略
    /// Melee 走范围判定，Ranged 走射线判定
    /// </summary>
    public enum WeaponType
    {
        Melee,   // 近战（锯子/刀）
        Ranged   // 枪械（步枪/手枪）
    }
}
