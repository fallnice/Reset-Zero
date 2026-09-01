namespace Combat
{
    /// <summary>
    /// 可受伤接口——敌人/可破坏物实现此接口以被武器命中
    /// 独立于 Role，敌人 AI 等模块可直接引用
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }
}
