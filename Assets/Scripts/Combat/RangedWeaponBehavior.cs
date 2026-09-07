using UnityEngine;

namespace Combat
{
    /// <summary>
    /// 枪械行为——Raycast 射线判定，命中首个可受伤目标
    /// </summary>
    public class RangedWeaponBehavior : IWeaponBehavior
    {
        public WeaponType Type => WeaponType.Ranged;

        public void Attack(Transform attacker, WeaponConfig weapon, float attackMultiplier, Vector3 aimDirection)
        {
            if (attacker == null || weapon == null) return;

            float damage = weapon.damage * attackMultiplier;

            // 瞄准方向优先（相机准星）；无有效方向时回退角色朝向
            Vector3 direction = aimDirection.sqrMagnitude > 0.0001f
                ? aimDirection.normalized
                : attacker.forward;

            // TODO(表现层): 射线起点改用枪口挂点，而非粗略的身高偏移
            Vector3 origin = attacker.position + Vector3.up * 1.2f;
            Ray ray = new Ray(origin, direction);

            if (Physics.Raycast(ray, out RaycastHit hit, weapon.range, weapon.hitMask))
            {
                // 过滤攻击者自身：枪口穿模不会打到自己
                if (hit.transform.IsChildOf(attacker)) return;

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                    target.TakeDamage(damage);
                // TODO(表现层): 命中特效 / 弹孔 / 音效
            }
        }
    }
}
