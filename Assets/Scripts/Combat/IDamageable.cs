namespace Combat
{
    /// <summary>
    /// 可受伤接口——敌人/可破坏物实现此接口以被武器命中
    /// 独立于 Role，敌人 AI 等模块可直接引用。
    /// 伤害以 DamageContext 传递，携带攻击者、阵营、命中点与方向等完整信息。
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(DamageContext context);
    }
}
