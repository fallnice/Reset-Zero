using System.Collections.Generic;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 近战行为——OverlapSphere 范围判定，命中范围内所有可受伤目标
    /// </summary>
    public class MeleeWeaponBehavior : IWeaponBehavior
    {
        public WeaponType Type => WeaponType.Melee;

        // 复用碰撞体缓冲区；攻击非每帧触发，无每帧 GC 压力
        private static readonly Collider[] _hitBuffer = new Collider[32];

        public void Attack(Transform attacker, WeaponConfig weapon, float attackMultiplier, Vector3 aimDirection)
        {
            if (attacker == null || weapon == null) return;

            float damage = weapon.damage * attackMultiplier;
            Faction sourceFaction = DamageContext.ResolveFaction(attacker);

            // 以角色前方为圆心扫一个球形范围（半径 = 攻击范围）
            Vector3 center = attacker.position + attacker.forward * (weapon.range * 0.5f);
            float radius = weapon.range;

            int count = Physics.OverlapSphereNonAlloc(center, radius, _hitBuffer, weapon.hitMask);

            // 同一目标（根对象）只扣一次血，避免其多个 Collider 被重复命中
            var damaged = new HashSet<IDamageable>();
            for (int i = 0; i < count; i++)
            {
                Collider hit = _hitBuffer[i];
                if (hit == null) continue;

                // 过滤攻击者自身：角色身上的碰撞体不会误伤自己
                if (hit.transform.IsChildOf(attacker)) continue;

                IDamageable target = hit.GetComponentInParent<IDamageable>();
                if (target == null) continue;
                if (!damaged.Add(target)) continue;

                Vector3 dir = hit.transform.position - attacker.position;
                if (dir.sqrMagnitude < 0.0001f) dir = attacker.forward;
                else dir.Normalize();

                target.TakeDamage(new DamageContext
                {
                    amount = damage,
                    attacker = attacker.gameObject,
                    sourceFaction = sourceFaction,
                    hitPoint = hit.transform.position,
                    direction = dir,
                });
            }
        }
    }
}
