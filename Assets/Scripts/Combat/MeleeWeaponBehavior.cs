using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 近战行为——OverlapSphere 范围判定，命中范围内所有可受伤目标
    /// </summary>
    public class MeleeWeaponBehavior : IWeaponBehavior
    {
        public WeaponType Type => WeaponType.Melee;

        public void Attack(Transform attacker, WeaponConfig weapon, float attackMultiplier)
        {
            if (attacker == null || weapon == null) return;

            float damage = weapon.damage * attackMultiplier;

            // 以角色前方为圆心扫一个球形范围（半径 = 攻击范围）
            Vector3 center = attacker.position + attacker.forward * (weapon.range * 0.5f);
            float radius = weapon.range;

            Collider[] hits = Physics.OverlapSphere(center, radius);
            foreach (Collider hit in hits)
            {
                if (hit == null) continue;

                // TODO: 攻击者自身实现 IDamageable 时需过滤，避免自伤
                // TODO: 同一目标多个 Collider 会重复扣血，正式版需按根对象去重
                IDamageable target = hit.GetComponentInParent<IDamageable>();
                if (target != null)
                    target.TakeDamage(damage);
            }
        }
    }
}
