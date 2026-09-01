using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 武器行为策略——近战/枪械各实现一套攻击逻辑
    /// 攻击只依赖 attacker（Transform）与 weapon（配置），不依赖 Role 具体类型，
    /// 保持 Combat → Role 单向依赖（Combat 不反向引用 Role）
    /// </summary>
    public interface IWeaponBehavior
    {
        WeaponType Type { get; }

        /// <summary> 执行一次攻击：命中判定 + 造成伤害 </summary>
        /// <param name="attackMultiplier">攻击力倍率（来自角色加成，1.0=无加成）</param>
        void Attack(Transform attacker, WeaponConfig weapon, float attackMultiplier);
    }
}
