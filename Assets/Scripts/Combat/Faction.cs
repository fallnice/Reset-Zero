namespace Combat
{
    /// <summary>
    /// 阵营——用于友伤判定与目标筛选。
    /// 同阵营（非 Neutral）之间默认不造成伤害，Neutral 不参与阵营判定。
    /// </summary>
    public enum Faction
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2,
    }
}
