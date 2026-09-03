using System;
using UnityEngine;
using Role.Core;
using Combat;
using Core;

namespace Role.Controllers
{
    /// <summary>
    /// 装备控制器——武器栏（求生之路式固定三槽）+ 武器切换中枢
    /// 逻辑层：槽位管理、切换过渡、行为策略、Blackboard 同步、事件广播
    /// 武器模型/装备动画留在 OnWeaponEquipped 钩子；角色姿态由实例事件交给 CharacterRoot 路由
    /// </summary>
    public class EquipmentController : MonoBehaviour, IStateResponder
    {
        [Header("表现层配置（回家后拖入）")]
        [SerializeField] private Transform rightHandAttachPoint; // 右手武器挂点

        private const float SWITCH_DURATION = 0.3f;       // 切换过渡时长（代码计时，不依赖动画）
        private const float DEFAULT_ATTACK_INTERVAL = 0.5f; // 旧资产缺少新字段时的安全回退值
        private const float MIN_ATTACK_INTERVAL = 0.01f;    // 防止异常倍率产生零间隔
        private const int SLOT_COUNT = 3;                    // 与 WeaponSlot 枚举一一对应

        private CharacterRoot _character;
        private CharacterStateCoordinator _coordinator;

        private readonly WeaponConfig[] _slots = new WeaponConfig[SLOT_COUNT];
        private readonly float[] _nextAttackAllowedTimes = new float[SLOT_COUNT];
        private WeaponSlot _currentSlot = WeaponSlot.Melee;
        private WeaponConfig _currentWeapon;
        private IWeaponBehavior _currentBehavior;

        private float _switchTimer;
        private WeaponConfig _pendingWeapon;

        /// <summary> 当前角色武器切换完成；供 CharacterRoot 路由内部表现状态 </summary>
        public event Action<WeaponConfig, WeaponConfig> WeaponEquipped;

        /// <summary> 当前角色成功提交一次攻击；冷却或状态阻断时不触发 </summary>
        public event Action<WeaponConfig> AttackCommitted;

        // ===== 对外查询 =====
        public WeaponSlot CurrentSlot => _currentSlot;
        public WeaponConfig CurrentWeapon => _currentWeapon;
        public IWeaponBehavior CurrentBehavior => _currentBehavior;
        public Transform RightHandAttachPoint => rightHandAttachPoint;
        public bool IsSwitching => _switchTimer > 0f;
        public bool UsesContinuousAttackInput => _currentWeapon != null
            && _currentWeapon.type == WeaponType.Ranged
            && _currentWeapon.isAutomatic;
        public float CurrentAttackInterval => GetEffectiveAttackInterval(_currentWeapon);
        public float AttackCooldownRemaining
        {
            get
            {
                if (_currentWeapon == null || !TryGetSlotIndex(_currentSlot, out int index)) return 0f;
                return Mathf.Max(0f, _nextAttackAllowedTimes[index] - Time.time);
            }
        }
        public bool CanAttack => _coordinator != null
            && _coordinator.CanAttack
            && !IsSwitching
            && _currentWeapon != null
            && _currentBehavior != null
            && AttackCooldownRemaining <= 0f;

        /// <summary> 由 CharacterRoot 在协调器创建完成后注入运行依赖 </summary>
        public void Init(CharacterRoot character, CharacterStateCoordinator coordinator)
        {
            _character = character;
            _coordinator = coordinator;

            if (_character == null)
                Debug.LogWarning("[EquipmentController] CharacterRoot 为空，攻击功能不可用", this);
            if (_coordinator == null)
                Debug.LogWarning("[EquipmentController] CharacterStateCoordinator 为空，攻击功能不可用", this);
        }

        private void Update()
        {
            if (!IsSwitching) return;

            _switchTimer -= Time.deltaTime;
            if (_switchTimer <= 0f)
                CompleteSwitch();
        }

        /// <summary> 拾取武器：落入归属槽位（同槽覆盖），并自动切换装备。Editor 调试菜单直接覆盖用 </summary>
        public bool Pickup(WeaponConfig weapon)
        {
            if (weapon == null || !TryGetSlotIndex(weapon.slot, out int index)) return false;

            // 只有真正换成另一把武器时才重置该槽冷却，重复拾取同一配置不能绕过冷却
            if (!ReferenceEquals(_slots[index], weapon))
                _nextAttackAllowedTimes[index] = 0f;

            _slots[index] = weapon;
            SwitchTo(weapon.slot);
            return true;
        }

        /// <summary> 目标槽位是否已有武器（供掉落物拾取与 UI 查询） </summary>
        public bool HasWeaponInSlot(WeaponSlot slot)
        {
            return TryGetSlotIndex(slot, out int index) && _slots[index] != null;
        }

        /// <summary>
        /// 运行时拾取入口：目标槽位已有武器时拒绝（不覆盖），空槽才落槽并切换。
        /// 与 Pickup 的区别在于——玩家从地图捡武器时不允许悄悄顶掉旧武器。
        /// </summary>
        public bool TryPickup(WeaponConfig weapon, out string failReason)
        {
            failReason = null;
            if (weapon == null)
            {
                failReason = "武器配置为空";
                return false;
            }
            if (!TryGetSlotIndex(weapon.slot, out int index))
            {
                failReason = "武器槽位非法";
                return false;
            }
            if (_slots[index] != null)
            {
                failReason = "该槽位已装备武器，请先丢弃旧武器";
                return false;
            }

            Pickup(weapon);
            return true;
        }

        /// <summary> 切换到指定槽位（空槽或当前武器忽略） </summary>
        public void SwitchTo(WeaponSlot slot)
        {
            if (!TryGetSlotIndex(slot, out int index)) return;

            WeaponConfig target = _slots[index];
            if (target == null) return;

            // 重复请求同一目标不重置计时；切回当前武器则取消尚未完成的切换
            if (ReferenceEquals(target, _pendingWeapon) && IsSwitching) return;
            if (ReferenceEquals(target, _currentWeapon))
            {
                _pendingWeapon = null;
                _switchTimer = 0f;
                return;
            }

            _pendingWeapon = target;
            _switchTimer = SWITCH_DURATION;
        }

        private void CompleteSwitch()
        {
            if (_pendingWeapon == null) return;

            WeaponConfig oldWeapon = _currentWeapon;
            WeaponConfig newWeapon = _pendingWeapon;

            _currentSlot = newWeapon.slot;
            _currentWeapon = newWeapon;
            _currentBehavior = CreateBehavior(newWeapon.type);
            _pendingWeapon = null;
            _switchTimer = 0f;

            Blackboard.Set("Weapon_CurrentType", newWeapon.type);
            OnWeaponEquipped(oldWeapon, newWeapon);
            WeaponEquipped?.Invoke(oldWeapon, newWeapon);
            EventBus.Emit(EventName.Weapon_Equipped, oldWeapon, newWeapon);
        }

        private static bool TryGetSlotIndex(WeaponSlot slot, out int index)
        {
            index = (int)slot;
            return index >= 0 && index < SLOT_COUNT;
        }

        private static IWeaponBehavior CreateBehavior(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Ranged:
                    return new RangedWeaponBehavior();
                case WeaponType.Melee:
                default:
                    return new MeleeWeaponBehavior();
            }
        }

        /// <summary> 尝试执行一次攻击；被角色状态、切换或冷却阻断时返回 false </summary>
        public bool Attack()
        {
            if (!CanAttack || _character == null) return false;
            if (!TryGetSlotIndex(_currentSlot, out int index)) return false;

            float attackMultiplier = Blackboard.Get(CombatKeys.AttackMultiplier, 1f);
            _currentBehavior.Attack(_character.transform, _currentWeapon, attackMultiplier);

            _nextAttackAllowedTimes[index] = Time.time + CurrentAttackInterval;
            AttackCommitted?.Invoke(_currentWeapon);
            return true;
        }

        /// <summary> 计算当前武器的实际攻击间隔；近战攻速倍率越高，间隔越短 </summary>
        private static float GetEffectiveAttackInterval(WeaponConfig weapon)
        {
            if (weapon == null) return 0f;

            float baseInterval = weapon.attackInterval > 0f
                ? weapon.attackInterval
                : DEFAULT_ATTACK_INTERVAL;

            if (weapon.type != WeaponType.Melee)
                return Mathf.Max(MIN_ATTACK_INTERVAL, baseInterval);

            float speedMultiplier = Blackboard.Get(CombatKeys.MeleeAttackSpeedMultiplier, 1f);
            if (speedMultiplier <= 0f)
                speedMultiplier = 1f;

            return Mathf.Max(MIN_ATTACK_INTERVAL, baseInterval / speedMultiplier);
        }

        /// <summary>
        /// 装备表现钩子：只负责武器模型与装备动画；角色状态联动由实例事件交给 CharacterRoot
        /// </summary>
        protected virtual void OnWeaponEquipped(WeaponConfig oldWeapon, WeaponConfig newWeapon)
        {
            // TODO(表现层): 在独立适配器中挂载模型并播放装备/收起动画。
            // 无动画时逻辑层仍由 SWITCH_DURATION 保证切换期间不可攻击。
        }

        // ===== IStateResponder =====
        public void OnStateEnter(CharacterState state)
        {
            // TODO(表现层): 特殊状态收起武器（如 Dead 时掉枪/收刀）
        }

        public void OnStateExit(CharacterState state) { }
    }
}
