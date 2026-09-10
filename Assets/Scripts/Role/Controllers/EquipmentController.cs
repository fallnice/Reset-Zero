using System;
using UnityEngine;
using Role.Core;
using Combat;
using Core;
using Interaction;

namespace Role.Controllers
{
    /// <summary>
    /// 装备控制器——武器栏（求生之路式固定三槽）+ 武器切换中枢
    /// 逻辑层：槽位管理、切换过渡、行为策略、事件广播
    /// 武器模型/装备动画留在 OnWeaponEquipped 钩子；角色姿态由实例事件交给 CharacterRoot 路由
    /// </summary>
    public class EquipmentController : MonoBehaviour, IStateResponder
    {
        [Header("表现层配置（回家后拖入）")]
        [SerializeField] private Transform rightHandAttachPoint; // 右手武器挂点

        [Header("换装过渡时长（表现层按此对齐动画播放速度，按手感调）")]
        [Tooltip("收起旧武器的时长；0 = 不做收武器表现，直接落位")]
        [SerializeField] private float putDuration = 0.55f;
        [Tooltip("掏出新武器的时长；0 = 不做掏出表现")]
        [SerializeField] private float takeDuration = 0.75f;

        private const float DEFAULT_ATTACK_INTERVAL = 0.5f; // 旧资产缺少新字段时的安全回退值
        private const float MIN_ATTACK_INTERVAL = 0.01f;    // 防止异常倍率产生零间隔
        private const float DROP_FORWARD_DISTANCE = 0.8f;    // 丢弃武器时掉落在玩家前方距离
        private const int SLOT_COUNT = 3;                    // 与 WeaponSlot 枚举一一对应

        // 空槽切换提示：预置常量而非运行时拼接，避免在输入路径上产生 GC；
        // 下标必须与 WeaponSlot（Primary=0 / Secondary=1 / Melee=2）严格对应
        private static readonly string[] EmptySlotToasts =
        {
            "「主武器」槽位没有武器",
            "「副武器」槽位没有武器",
            "「近战」槽位没有武器"
        };

        // 「已经拿着这把武器」的提示前缀（后缀由武器名 + 一个右书名号补全）。
        // 与 EmptySlotToasts 同一个理由：同槽重复切换同样不能静默——
        // 「我按了 1 却没反应」里有一半其实是「本来就已经拿着枪」，玩家分不出来。
        private const string AlreadyEquippedToastPrefix = "已经拿着「";

        /// <summary> 换装过渡的两个阶段；None 表示空闲——只有空闲时才允许攻击 </summary>
        private enum SwitchPhase { None, Put, Take }

        private CharacterRoot _character;
        private CharacterStateCoordinator _coordinator;

        private readonly WeaponConfig[] _slots = new WeaponConfig[SLOT_COUNT];
        private readonly float[] _nextAttackAllowedTimes = new float[SLOT_COUNT];
        private WeaponSlot _currentSlot = WeaponSlot.Melee;
        private WeaponConfig _currentWeapon;
        private IWeaponBehavior _currentBehavior;

        private float _switchTimer;
        private WeaponConfig _pendingWeapon;

        // 换装分两段：Put（收旧武器，此时动画集还没换）→ 落位 → Take（掏新武器）。
        // 只用一个计时器 + 一个阶段标志，不给两段各维护一份状态。
        private SwitchPhase _phase = SwitchPhase.None;

        /// <summary> 当前角色武器切换完成；供 CharacterRoot 路由内部表现状态 </summary>
        public event Action<WeaponConfig, WeaponConfig> WeaponEquipped;

        /// <summary> 即将收起当前武器（换装第一段开始）；表现层据此在旧动画集上播「收武器」 </summary>
        public event Action<WeaponConfig> UnequipStarted;

        /// <summary> 当前角色成功提交一次攻击；冷却或状态阻断时不触发 </summary>
        public event Action<WeaponConfig> AttackCommitted;

        // ===== 对外查询 =====
        public WeaponSlot CurrentSlot => _currentSlot;
        public WeaponConfig CurrentWeapon => _currentWeapon;
        public IWeaponBehavior CurrentBehavior => _currentBehavior;
        public Transform RightHandAttachPoint => rightHandAttachPoint;
        public bool IsSwitching => _phase != SwitchPhase.None;

        /// <summary> 收起旧武器的时长；表现层按此对齐收武器动画的播放速度 </summary>
        public float PutDuration => putDuration;

        /// <summary> 掏出新武器的时长；表现层按此对齐掏出动画的播放速度 </summary>
        public float TakeDuration => takeDuration;
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
            if (_phase == SwitchPhase.None) return;

            _switchTimer -= Time.deltaTime;
            if (_switchTimer > 0f) return;

            if (_phase == SwitchPhase.Put)
            {
                // 收武器演完才真正落位：此刻才换动画集与武器模型，并由 ApplyEquipped 进入 Take 阶段
                CompleteSwitch();
                return;
            }

            // Take 演完：恢复可攻击
            _phase = SwitchPhase.None;
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

        /// <summary> 读取指定槽位里的武器；空槽或槽位非法返回 null（供武器栏 UI 与调试查询） </summary>
        public WeaponConfig GetWeaponInSlot(WeaponSlot slot)
        {
            return TryGetSlotIndex(slot, out int index) ? _slots[index] : null;
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

        /// <summary>
        /// 丢弃当前武器：清空该槽位、在玩家前方生成可再拾取的掉落物，
        /// 然后自动接替剩余槽位里的武器（见 FindFallbackWeapon）。无当前武器时返回 false 并提示。
        /// </summary>
        public bool Drop()
        {
            if (_currentWeapon == null)
            {
                EventBus.Emit(EventName.UI_Toast, "当前没有可丢弃的武器");
                return false;
            }
            if (!TryGetSlotIndex(_currentSlot, out int index)) return false;

            WeaponConfig dropped = _currentWeapon;

            _slots[index] = null;
            _nextAttackAllowedTimes[index] = 0f;

            SpawnDroppedPickup(dropped);

            // 丢完立刻接替剩余槽位里的武器：近战（Weapon_Saw）是开局默认装备、一直压在 Melee 槽里，
            // 所以丢枪的正确表现是「掏出刀」而不是「变成空手」。三个槽都空才是真·空手（newWeapon=null）。
            //
            // 这里直接落位、不走 SWITCH_DURATION：丢弃是瞬时行为，拖 0.3s 会变成
            // 「枪已经躺地上了、手里还举着枪」，动画集与武器模型都要等半拍才跟。
            ApplyEquipped(dropped, FindFallbackWeapon());

            EventBus.Emit(EventName.Weapon_Dropped, dropped);
            return true;
        }

        /// <summary>
        /// 丢弃后要接替的武器：优先另一个枪械槽（保住远程档位），都没有才回退近战槽。
        /// 返回 null 表示三个槽全空——真·空手。
        /// </summary>
        private WeaponConfig FindFallbackWeapon()
        {
            WeaponConfig melee = null;

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                WeaponConfig candidate = _slots[i];
                if (candidate == null) continue;

                // 近战只作为兜底先记下不返回：手上还有枪时不该因为丢了一把就掉到刀
                if (candidate.type == WeaponType.Melee)
                {
                    melee = candidate;
                    continue;
                }

                return candidate;
            }

            return melee;
        }

        /// <summary> 在玩家前方地面生成可拾取的武器掉落物（无模型时仍可交互拾取） </summary>
        private void SpawnDroppedPickup(WeaponConfig weapon)
        {
            Vector3 origin = _character != null ? _character.transform.position : transform.position;
            Vector3 forward = _character != null ? _character.transform.forward : transform.forward;
            Vector3 spawnPos = origin + forward * DROP_FORWARD_DISTANCE + Vector3.up * 0.5f;

            // 向下贴地，避免掉落物悬空；贴不到地则保留计算位置
            if (Physics.Raycast(spawnPos, Vector3.down, out RaycastHit hit, 1.5f))
                spawnPos = hit.point + Vector3.up * 0.15f;

            GameObject pickup = new GameObject($"Dropped_{weapon.weaponName}");
            pickup.transform.position = spawnPos;

            // 可交互碰撞体：供 InteractionDetector 的 OverlapSphere 检测到
            SphereCollider col = pickup.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.5f;

            WeaponPickupItem item = pickup.AddComponent<WeaponPickupItem>();
            item.SetWeaponConfig(weapon);

            // 视觉模型：回家配置 modelPrefab 后可见；无模型时逻辑仍可拾取
            if (weapon.modelPrefab != null)
            {
                GameObject model = Instantiate(weapon.modelPrefab, pickup.transform);
                model.transform.localPosition = Vector3.zero;
            }
        }

        /// <summary> 切换到指定槽位（空槽或当前武器忽略） </summary>
        public void SwitchTo(WeaponSlot slot)
        {
            if (!TryGetSlotIndex(slot, out int index)) return;

            WeaponConfig target = _slots[index];
            if (target == null)
            {
                // 不能静默 return：玩家分不清「这个槽是空的」和「按键没生效」，
                // 两种情况在表现上一模一样（按了没反应），会被当成输入失灵。
                EventBus.Emit(EventName.UI_Toast, EmptySlotToasts[index]);
                return;
            }

            // 重复请求同一目标：不重置已经开始的收武器计时
            if (_phase == SwitchPhase.Put && ReferenceEquals(target, _pendingWeapon)) return;

            if (ReferenceEquals(target, _currentWeapon))
            {
                // Take 阶段目标就是手上这把：等掏出动作演完，提前取消会变成「抽到一半又放下」
                if (_phase == SwitchPhase.Take) return;

                CancelSwitch();

                // 「已经拿着它」不能静默：玩家会把它读成「按键没生效」。
                // 拼接只发生在按 1/2/3 与拾取这类离散输入上，不在每帧热点里。
                EventBus.Emit(EventName.UI_Toast, AlreadyEquippedToastPrefix + target.weaponName + "」");
                return;
            }

            BeginSwitch(target);
        }

        /// <summary> 开始换装第一段：演「收武器」；没武器可收或 putDuration 为 0 时跳过，直接落位 </summary>
        private void BeginSwitch(WeaponConfig target)
        {
            // _currentWeapon == null 覆盖「开局首次装备」与「空手捡枪」：手上本来就空的，
            // 没有收武器可演；否则出生后要白等 0.55 秒才拿上武器
            if (_currentWeapon == null || putDuration <= 0f)
            {
                ApplyEquipped(_currentWeapon, target);
                return;
            }

            _pendingWeapon = target;
            _phase = SwitchPhase.Put;
            _switchTimer = putDuration;

            // 此时动画集还是旧的：表现层在旧动画集上播收武器，收完才由 CompleteSwitch 落位
            UnequipStarted?.Invoke(_currentWeapon);
        }

        /// <summary> 取消尚未落位的切换（只可能发生在 Put 阶段） </summary>
        private void CancelSwitch()
        {
            _pendingWeapon = null;
            _phase = SwitchPhase.None;
            _switchTimer = 0f;
        }

        private void CompleteSwitch()
        {
            WeaponConfig target = _pendingWeapon;
            _pendingWeapon = null;

            if (target == null)
            {
                _phase = SwitchPhase.None;
                return;
            }

            ApplyEquipped(_currentWeapon, target);
        }

        /// <summary>
        /// 让一次装备变更真正生效：写入当前槽位/武器/行为策略，并广播三层事件。
        /// 由「切换完成」与「丢弃后自动接替」共用；newWeapon 为 null 表示空手，此时槽位索引保持不变。
        /// 落位后自动进入 Take 阶段（掏出新武器），掏出途中 IsSwitching 为 true，不能攻击。
        /// </summary>
        private void ApplyEquipped(WeaponConfig oldWeapon, WeaponConfig newWeapon)
        {
            if (newWeapon != null)
                _currentSlot = newWeapon.slot;

            _currentWeapon = newWeapon;
            _currentBehavior = newWeapon != null ? CreateBehavior(newWeapon.type) : null;
            _pendingWeapon = null;

            OnWeaponEquipped(oldWeapon, newWeapon);
            WeaponEquipped?.Invoke(oldWeapon, newWeapon);
            EventBus.Emit(EventName.Weapon_Equipped, oldWeapon, newWeapon);

            // 落位后统一进入「掏出新武器」阶段——表现层已在 WeaponEquipped 回调里触发了掏武器动画，
            // 这里只负责让逻辑层继续锁住攻击，直到动画演完
            _phase = takeDuration > 0f ? SwitchPhase.Take : SwitchPhase.None;
            _switchTimer = takeDuration > 0f ? takeDuration : 0f;
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

            float attackMultiplier = _character.Context.CombatStats.attackMultiplier;
            Vector3 aimDirection = _character.GetAimDirection();
            _currentBehavior.Attack(_character.transform, _currentWeapon, attackMultiplier, aimDirection);

            _nextAttackAllowedTimes[index] = Time.time + CurrentAttackInterval;
            AttackCommitted?.Invoke(_currentWeapon);
            return true;
        }

        /// <summary> 计算当前武器的实际攻击间隔；近战攻速倍率越高，间隔越短 </summary>
        private float GetEffectiveAttackInterval(WeaponConfig weapon)
        {
            if (weapon == null) return 0f;

            float baseInterval = weapon.attackInterval > 0f
                ? weapon.attackInterval
                : DEFAULT_ATTACK_INTERVAL;

            if (weapon.type != WeaponType.Melee)
                return Mathf.Max(MIN_ATTACK_INTERVAL, baseInterval);

            // 攻速倍率来自角色实例 CombatStats；未注入角色时回退 1.0
            float speedMultiplier = _character != null
                ? _character.Context.CombatStats.meleeAttackSpeedMultiplier
                : 1f;
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
