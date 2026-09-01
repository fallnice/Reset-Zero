using UnityEngine;
using Role.Core;
using Combat;
using Core;

namespace Role.Controllers
{
    /// <summary>
    /// 装备控制器——武器栏（求生之路式固定三槽）+ 武器切换中枢
    /// 逻辑层：槽位管理、切换过渡、行为策略、Blackboard 同步、事件广播
    /// 表现层（挂模型/播动画/驱动上半身状态机）留在 OnWeaponEquipped 钩子，回家对齐 Animator 后实现
    /// </summary>
    public class EquipmentController : MonoBehaviour, IStateResponder
    {
        [Header("表现层配置（回家后拖入）")]
        [SerializeField] private Transform rightHandAttachPoint; // 右手武器挂点

        private const float SWITCH_DURATION = 0.3f; // 切换过渡时长（代码计时，不依赖动画）
        private const int SLOT_COUNT = 3;           // 与 WeaponSlot 枚举一一对应

        private CharacterRoot _character;
        private CharacterStateCoordinator _coordinator;

        private readonly WeaponConfig[] _slots = new WeaponConfig[SLOT_COUNT];
        private WeaponSlot _currentSlot = WeaponSlot.Melee;
        private IWeaponBehavior _currentBehavior;

        private float _switchTimer;
        private WeaponConfig _pendingWeapon;

        // ===== 对外查询 =====
        public WeaponSlot CurrentSlot => _currentSlot;
        public WeaponConfig CurrentWeapon => _slots[(int)_currentSlot];
        public IWeaponBehavior CurrentBehavior => _currentBehavior;
        public Transform RightHandAttachPoint => rightHandAttachPoint;
        public bool IsSwitching => _switchTimer > 0f;
        public bool CanAttack => _coordinator != null && _coordinator.CanAttack && !IsSwitching;

        private void Awake()
        {
            _character = GetComponentInParent<CharacterRoot>();
            _coordinator = GetComponentInParent<CharacterStateCoordinator>();
        }

        private void Update()
        {
            if (!IsSwitching) return;

            _switchTimer -= Time.deltaTime;
            if (_switchTimer <= 0f)
                CompleteSwitch();
        }

        /// <summary> 拾取武器：落入归属槽位（同槽覆盖），并自动切换装备 </summary>
        public bool Pickup(WeaponConfig weapon)
        {
            if (weapon == null) return false;

            _slots[(int)weapon.slot] = weapon;
            SwitchTo(weapon.slot);
            return true;
        }

        /// <summary> 切换到指定槽位（空槽忽略） </summary>
        public void SwitchTo(WeaponSlot slot)
        {
            int index = (int)slot;
            WeaponConfig target = _slots[index];
            if (target == null) return;

            // 已是当前武器且已装备，无需重复切换
            if (slot == _currentSlot && _currentBehavior != null && !IsSwitching) return;

            _pendingWeapon = target;
            _switchTimer = SWITCH_DURATION;
        }

        private void CompleteSwitch()
        {
            if (_pendingWeapon == null) return;

            WeaponConfig oldWeapon = CurrentWeapon;
            _currentSlot = _pendingWeapon.slot;
            _currentBehavior = CreateBehavior(_pendingWeapon.type);

            Blackboard.Set("Weapon_CurrentType", _pendingWeapon.type);

            _pendingWeapon = null;

            OnWeaponEquipped(oldWeapon, CurrentWeapon);
            EventBus.Emit(EventName.Weapon_Equipped, oldWeapon, CurrentWeapon);
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

        /// <summary> 攻击入口（供攻击状态/输入层调用） </summary>
        public void Attack()
        {
            if (!CanAttack) return;

            WeaponConfig weapon = CurrentWeapon;
            if (weapon == null || _currentBehavior == null || _character == null) return;

            float attackMultiplier = Blackboard.Get(CombatKeys.AttackMultiplier, 1f);
            _currentBehavior.Attack(_character.transform, weapon, attackMultiplier);
        }

        /// <summary>
        /// 表现层钩子：武器切换完成后的模型/动画/上半身状态联动（回家对齐 Animator 后实现）
        /// </summary>
        protected virtual void OnWeaponEquipped(WeaponConfig oldWeapon, WeaponConfig newWeapon)
        {
            // TODO(表现层):
            // 1. 挂模型：销毁旧模型，把 newWeapon.modelPrefab 实例化到 rightHandAttachPoint
            // 2. 动画：PlayEquip / PlayUnequip（无动画则瞬切，逻辑层已用 SWITCH_DURATION 兜底）
            // 3. 上半身状态机联动：
            //      Ranged → _character.upperBodySM.ToAim()  （持枪瞄准）
            //      Melee  → _character.upperBodySM.ToIdle() （空手）
            // 4. Animator 姿态参数：SetInteger("WeaponPose", newWeapon.animPoseParam)
        }

        // ===== IStateResponder =====
        public void OnStateEnter(CharacterState state)
        {
            // TODO(表现层): 特殊状态收起武器（如 Dead 时掉枪/收刀）
        }

        public void OnStateExit(CharacterState state) { }
    }
}
