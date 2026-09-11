using UnityEngine;
using Core;
using Role.Core;
using Role.States;
using Combat;

namespace Role
{
    /// <summary>
    /// 角色根节点——统筹所有角色相关模块，纯代码驱动位移
    /// 挂载在角色 Prefab 的根 GameObject 上
    /// 需要 CharacterController 组件
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CharacterRoot : MonoBehaviour, IFactionMember, IAimStateProvider
    {
        // ===== 输入 =====
        [Tooltip("可选：显式指定实现 IInputProvider 的组件；未指定时自动查找且要求角色下只有一个实现")]
        [SerializeField] private MonoBehaviour inputProviderSource;
        public IInputProvider inputProvider { get; private set; }

        /// <summary> 角色 Animator（自动获取子级） </summary>
        public Animator Animator { get; private set; }

        [Header("配置")]
        [SerializeField] private CharacterConfig config;
        /// <summary> 角色配置数据（速度/重力/转身等），必须拖入 </summary>
        public CharacterConfig Config => config;

        [Header("控制身份")]
        [Tooltip("玩家角色响应 UI 模态（打开背包/制作时站住）；AI/敌人应关闭此开关")]
        [SerializeField] private bool isPlayerControlled = true;
        /// <summary> 是否为玩家控制角色；AI 输入源会覆盖旧 Prefab 遗留的 true 默认值 </summary>
        public bool IsPlayerControlled => isPlayerControlled && !(inputProvider is IAIInputProvider);

        [Header("阵营")]
        [SerializeField] private Faction faction = Faction.Player;
        /// <summary> 角色阵营（用于友伤判定与目标筛选） </summary>
        public Faction Faction => faction;

        [Header("协调器")]
        [SerializeField] private CharacterStateCoordinator coordinator;

        /// <summary> 角色共享上下文（运行时数据 + 战斗属性），由根节点组装并注入给状态机/控制器 </summary>
        public CharacterContext Context { get; private set; }

        // 状态机是纯 C# 类，不挂 GameObject，Awake 中 new
        public StateMachine.FullBodyStateMachine fullBodySM;
        public StateMachine.UpperBodyStateMachine upperBodySM;
        private StateMachine.IUpperBodyAnimationSink _upperBodyAnimationSink;
        private Controllers.CharacterAnimationController _animationController;

        // UI 模态（背包/制作等面板打开）时阻断战斗输入
        private bool _uiModalOpen;
        private bool _movementWasAllowed;
        private EventBus.SubscriptionToken _uiModalToken;
        private bool _runtimeEventsSubscribed;
        private bool _started;
        private bool _initialWeaponsApplied;

        // 开火后自动进入瞄准状态的剩余时长：由 HandleAttackCommitted 重置，UpdateAimState 每帧递减
        private float _autoAimRemain;

        [Header("子控制器（可选，未拖拽则自动查找）")]
        [SerializeField] private Controllers.EquipmentController equipmentCtrl;
        [SerializeField] private Controllers.IKController ikCtrl;
        [SerializeField] private Controllers.ExpressionController expressionCtrl;
        [SerializeField] private Controllers.AudioController audioCtrl;

        [Header("生命（可选，未拖拽则自动查找）")]
        [SerializeField] private HealthController health;
        /// <summary> 生命组件（未找到时为 null） </summary>
        public HealthController Health => health;

        /// <summary> 当前强状态是否允许移动；导航据此暂停卡住计时 </summary>
        public bool CanMove => coordinator == null || coordinator.CanMove;

        // ===== Unity 生命周期 =====

        private void Awake()
        {
            ResolveInputProvider();

            // 获取 Animator 与可选的上半身动画适配器（含初始 inactive 的子模型）
            Animator = GetComponentInChildren<Animator>(true);
            _upperBodyAnimationSink = GetComponentInChildren<StateMachine.IUpperBodyAnimationSink>(true);
            _animationController = GetComponentInChildren<Controllers.CharacterAnimationController>(true);
            EnsureAnimatorEventRelay();

            // 获取或自动添加协调器
            if (coordinator == null)
                coordinator = GetComponentInChildren<CharacterStateCoordinator>(true);
            if (coordinator == null)
                coordinator = gameObject.AddComponent<CharacterStateCoordinator>();

            // 创建状态机（纯 C# 类，不是 MonoBehaviour）
            fullBodySM = new StateMachine.FullBodyStateMachine();
            upperBodySM = new StateMachine.UpperBodyStateMachine();

            // 组装角色共享上下文（实例级运行时数据 + 战斗属性，替代静态 Blackboard）
            Context = new CharacterContext(new CharacterRuntimeData(), new CombatStats());

            // 获取子控制器（全部可选，缺失只 log 不报错）
            if (equipmentCtrl == null)
                equipmentCtrl = GetComponentInChildren<Controllers.EquipmentController>(true);
            if (ikCtrl == null)
                ikCtrl = GetComponentInChildren<Controllers.IKController>(true);
            if (expressionCtrl == null)
                expressionCtrl = GetComponentInChildren<Controllers.ExpressionController>(true);
            if (audioCtrl == null)
                audioCtrl = GetComponentInChildren<Controllers.AudioController>(true);

            // 获取生命组件（可选，未挂 HealthController 时角色不可受伤）
            if (health == null)
                health = GetComponentInChildren<HealthController>(true);

            // 由根节点显式注入依赖，避免子控制器 Awake 顺序不确定
            if (equipmentCtrl != null)
                equipmentCtrl.Init(this, coordinator);

            // 检查 CharacterController
            if (GetComponent<CharacterController>() == null)
                Debug.LogError("[CharacterRoot] 缺少 CharacterController 组件，角色无法移动", this);

            // 检查 Config
            if (config == null)
                Debug.LogError("[CharacterRoot] 未分配 CharacterConfig，请在 Inspector 拖入", this);
        }

        /// <summary> 解析唯一输入源；显式引用优先，自动查找时检测多实现歧义 </summary>
        private void ResolveInputProvider()
        {
            if (inputProviderSource != null)
            {
                inputProvider = inputProviderSource as IInputProvider;
                if (inputProvider == null)
                    Debug.LogError("[CharacterRoot] Input Provider Source 未实现 IInputProvider", this);
                return;
            }

            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            int matchCount = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is IInputProvider candidate)) continue;
                if (inputProvider == null) inputProvider = candidate;
                matchCount++;
            }

            if (matchCount > 1)
                Debug.LogError("[CharacterRoot] 检测到多个 IInputProvider，请显式指定 Input Provider Source", this);
        }

        /// <summary>
        /// Animation Event 只会发给 Animator 所在 GameObject；运行时补桥接器，
        /// 保证 CharacterRoot 与 Animator 分层时仍能接收插槽、脚步与 Root Motion 回调。
        /// </summary>
        private void EnsureAnimatorEventRelay()
        {
            if (Animator == null) return;

            Controllers.CharacterAnimatorEventRelay relay =
                Animator.GetComponent<Controllers.CharacterAnimatorEventRelay>();
            if (relay == null)
                relay = Animator.gameObject.AddComponent<Controllers.CharacterAnimatorEventRelay>();
            relay.Initialize(this);
        }

        private void OnEnable()
        {
            SubscribeRuntimeEvents();
            if (!_started) return;

            RegisterResponders();
            if (health != null && health.IsDead)
                Die();
            else if (health != null && coordinator != null && coordinator.IsDead)
                RestoreAfterHealthReset();
        }

        private void OnDisable()
        {
            UnsubscribeRuntimeEvents();
            UnregisterResponders();
        }

        private void Start()
        {
            _started = true;
            RegisterResponders();

            // 初始化状态机（注入 character + coordinator；动画 Sink 可为空）
            fullBodySM.Init(this, coordinator);
            upperBodySM.Init(this, coordinator, _upperBodyAnimationSink);

            // 设置初始状态，并同步可能在 Start 前已装备的武器
            fullBodySM.ToIdle();
            upperBodySM.SetMode(GetUpperBodyMode(equipmentCtrl?.CurrentWeapon));
            _movementWasAllowed = CanMove && !_uiModalOpen;

            // 补偿 OnEnable/Start 之前可能发生或禁用期间错过的生命事件。
            if (health != null && health.IsDead)
                Die();
            else if (health != null && coordinator != null && coordinator.IsDead)
                RestoreAfterHealthReset();

            EquipInitialWeapons();
        }

        /// <summary>
        /// 开局装备 CharacterConfig 里配置的初始武器——复用拾取链路（落槽 → 切换 → 广播 WeaponEquipped），
        /// 因此动画集、上半身姿态、武器模型都会跟着走正常流程；未配置则出生空手。
        /// </summary>
        private void EquipInitialWeapons()
        {
            if (_initialWeaponsApplied) return;

            // CharacterConfig.initialWeapons 是玩家出生装备；AI/敌人由 EnemyConfig 独立配置，避免复用 Player.asset 时串装备。
            if (!IsPlayerControlled)
            {
                _initialWeaponsApplied = true;
                return;
            }
            if (equipmentCtrl == null || config == null || config.initialWeapons == null)
            {
                _initialWeaponsApplied = true;
                return;
            }
            // Start 前死亡时暂不消费初始化机会，Respawn 后再补装。
            if (coordinator != null && !coordinator.CanChangeEquipment) return;

            WeaponConfig[] weapons = config.initialWeapons;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] != null)
                    equipmentCtrl.Pickup(weapons[i]);
            }
            _initialWeaponsApplied = true;
        }

        private void Update()
        {
            bool canAttack = coordinator == null || coordinator.CanAttack;
            upperBodySM.SetSuppressed(!canAttack);
            HandleCombatInput();

            // 瞄准状态依赖本帧的开火结果，故放在战斗输入处理之后计算
            UpdateAimState();

            // 死亡/眩晕/过场/UI 模态等状态下停止移动状态机
            bool canMove = (coordinator == null || coordinator.CanMove) && !_uiModalOpen;
            if (canMove)
            {
                // 朝向先于移动状态机确定：瞄准时锁相机方向，Walk/Run 只管位移
                UpdateFacing();
                fullBodySM.OnUpdate();
            }
            else if (_movementWasAllowed)
            {
                // 首次进入禁移状态时复位 locomotion，避免 UI/眩晕/死亡后原地播放 Walk/Run。
                fullBodySM.ToIdle();
            }
            _movementWasAllowed = canMove;

            // 抑制只关闭叠加表现，请求姿态保留，恢复后自动同步
            upperBodySM.OnUpdate();
        }

        /// <summary> 将抽象输入命令路由给装备模块；槽位切换优先于同帧攻击 </summary>
        private void HandleCombatInput()
        {
            if (inputProvider == null || equipmentCtrl == null) return;

            // UI 模态或死亡/眩晕/过场等强状态下阻断攻击、切枪与丢弃
            if (_uiModalOpen || (coordinator != null && !coordinator.CanChangeEquipment)) return;

            if (inputProvider.SelectPrimaryPressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Primary);
            else if (inputProvider.SelectSecondaryPressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Secondary);
            else if (inputProvider.SelectMeleePressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Melee);

            if (inputProvider.DropWeaponPressedThisFrame)
                equipmentCtrl.Drop();

            bool shouldAttack = inputProvider.AttackPressedThisFrame
                || (equipmentCtrl.UsesContinuousAttackInput && inputProvider.AttackHeld);
            if (shouldAttack)
                equipmentCtrl.Attack();
        }

        /// <summary> 将装备完成事实映射为上半身持续姿态 </summary>
        private void HandleWeaponEquipped(WeaponConfig oldWeapon, WeaponConfig newWeapon)
        {
            upperBodySM?.SetMode(GetUpperBodyMode(newWeapon));
        }

        /// <summary> 只有成功提交的远程攻击才产生一次上半身开火动作 </summary>
        private void HandleAttackCommitted(WeaponConfig weapon)
        {
            if (weapon == null || weapon.type != WeaponType.Ranged) return;
            upperBodySM?.TryPlayAction(StateMachine.UpperBodyAction.Fire);

            // 开火即进入瞄准状态；松开瞄准键后由该计时器维持一小段再自动退出
            if (config != null)
                _autoAimRemain = config.aimAutoHoldSeconds;
        }

        /// <summary>
        /// 聚合「是否处于瞄准状态」：需持有远程武器，且（按住瞄准键 或 处于开火后的自动保持期内）。
        /// 每帧在 HandleCombatInput 之后调用，确保本帧刚提交的开火能当帧进入瞄准。
        /// </summary>
        private void UpdateAimState()
        {
            WeaponConfig weapon = equipmentCtrl != null ? equipmentCtrl.CurrentWeapon : null;
            bool rangedEquipped = weapon != null && weapon.type == WeaponType.Ranged;
            bool aimHeld = inputProvider != null && inputProvider.AimHeld;
            bool canAct = (coordinator == null || coordinator.CanAttack) && !_uiModalOpen;

            IsAiming = canAct && rangedEquipped && (aimHeld || _autoAimRemain > 0f);

            // 同步给上半身状态机（SetMode 内部已去重，仅在姿态真正变化时才通知动画层）：
            // 枪械动画集据此在「瞄准套 / 未瞄准套」两套子状态机之间切换
            upperBodySM?.SetMode(GetUpperBodyMode(weapon));

            if (_autoAimRemain > 0f)
                _autoAimRemain -= Time.deltaTime;
        }

        /// <summary> 生命归零时进入死亡状态（由 HealthController.Died 触发） </summary>
        private void HandleHealthDied()
        {
            Die();
        }

        private void HandleHealthReset()
        {
            RestoreAfterHealthReset();
        }

        /// <summary>
        /// 装备事实 + 瞄准状态 → 上半身持续姿态：
        /// 非远程武器 → Inactive；远程未瞄准 → RangedReady；远程且瞄准中 → RangedAiming。
        /// </summary>
        private StateMachine.UpperBodyMode GetUpperBodyMode(WeaponConfig weapon)
        {
            if (weapon == null || weapon.type != WeaponType.Ranged)
                return StateMachine.UpperBodyMode.Inactive;

            return IsAiming
                ? StateMachine.UpperBodyMode.RangedAiming
                : StateMachine.UpperBodyMode.RangedReady;
        }

        private void LateUpdate()
        {
            // IK 必须在 Animator 之后执行
            ikCtrl?.OnLateUpdate();
        }

        private void OnDestroy()
        {
            UnsubscribeRuntimeEvents();
            UnregisterResponders();
        }

        private void SubscribeRuntimeEvents()
        {
            if (_runtimeEventsSubscribed) return;

            if (equipmentCtrl != null)
            {
                equipmentCtrl.WeaponEquipped += HandleWeaponEquipped;
                equipmentCtrl.AttackCommitted += HandleAttackCommitted;
            }
            if (health != null)
            {
                health.Died += HandleHealthDied;
                health.HealthReset += HandleHealthReset;
            }

            // 只有玩家角色订阅 UI 模态；AI/敌人不因玩家打开面板而停止。
            if (IsPlayerControlled && _uiModalToken == null)
            {
                _uiModalToken = EventBus.Subscribe(EventName.UI_ModalChanged, args =>
                {
                    _uiModalOpen = args != null && args.Length > 0 && args[0] is bool b && b;
                });
            }

            _runtimeEventsSubscribed = true;
        }

        private void UnsubscribeRuntimeEvents()
        {
            if (_runtimeEventsSubscribed)
            {
                if (equipmentCtrl != null)
                {
                    equipmentCtrl.WeaponEquipped -= HandleWeaponEquipped;
                    equipmentCtrl.AttackCommitted -= HandleAttackCommitted;
                }
                if (health != null)
                {
                    health.Died -= HandleHealthDied;
                    health.HealthReset -= HandleHealthReset;
                }
                _runtimeEventsSubscribed = false;
            }

            _uiModalToken?.Dispose();
            _uiModalToken = null;
            _uiModalOpen = false;
        }

        private void RegisterResponders()
        {
            if (coordinator == null) return;
            if (equipmentCtrl != null) coordinator.Register(equipmentCtrl);
            if (ikCtrl != null) coordinator.Register(ikCtrl);
            if (expressionCtrl != null) coordinator.Register(expressionCtrl);
            if (audioCtrl != null) coordinator.Register(audioCtrl);
        }

        private void UnregisterResponders()
        {
            if (coordinator == null) return;
            if (equipmentCtrl != null) coordinator.Unregister(equipmentCtrl);
            if (ikCtrl != null) coordinator.Unregister(ikCtrl);
            if (expressionCtrl != null) coordinator.Unregister(expressionCtrl);
            if (audioCtrl != null) coordinator.Unregister(audioCtrl);
        }

        // ===== 公共接口 =====

        /// <summary> 装备控制器（子控制器，未拖拽且未自动找到时为 null） </summary>
        public Controllers.EquipmentController Equipment => equipmentCtrl;

        /// <summary> FullBody 状态写入基础移动动画；有表现适配器时按参数契约安全写入 </summary>
        public void SetLocomotionAnimation(float speed, bool isGrounded)
        {
            if (_animationController != null)
            {
                _animationController.SetLocomotion(speed, isGrounded);
                return;
            }

            // 兼容尚未挂表现适配器的旧角色 Controller（旧契约固定包含 Speed/IsGrounded）。
            if (Animator != null)
            {
                Animator.SetFloat("Speed", speed);
                Animator.SetBool("IsGrounded", isGrounded);
            }
        }

        /// <summary> 跳跃/下落状态更新落地参数 </summary>
        public void SetGroundedAnimation(bool isGrounded)
        {
            if (_animationController != null)
            {
                _animationController.SetGrounded(isGrounded);
                return;
            }
            Animator?.SetBool("IsGrounded", isGrounded);
        }

        /// <summary>
        /// 当前是否处于瞄准状态（IAimStateProvider）：持远程武器，且（按住瞄准键 或 开火后的自动保持期内）。
        /// 每帧由 UpdateAimState 计算，供相机拉近、准星等表现方读取。
        /// </summary>
        public bool IsAiming { get; private set; }

        /// <summary>
        /// UI 模态（背包/制作等面板）是否正在阻断战斗输入。
        /// 为 true 时按 1/2/3、丢弃、攻击都会被 HandleCombatInput 直接挡掉，
        /// 表现和输入失灵一模一样——排查「按键没反应」先看这个。
        /// </summary>
        public bool IsUiInputBlocked => _uiModalOpen;

        /// <summary> 当前瞄准方向（世界空间，已归一化）；无输入时回退角色朝向 </summary>
        public Vector3 GetAimDirection()
        {
            if (inputProvider != null)
            {
                Vector3 dir = inputProvider.LookDirection;
                if (dir.sqrMagnitude > 0.0001f)
                    return dir.normalized;
            }
            return transform.forward;
        }

        /// <summary>
        /// 平滑转向目标方向（只取水平面，保持角色不抬头/低头）
        /// </summary>
        public void RotateToward(Vector3 direction)
        {
            if (config == null) return;
            RotateToward(direction, config.rotationSpeed);
        }

        /// <summary> 平滑转向目标方向，用指定角速度系数 </summary>
        private void RotateToward(Vector3 direction, float rotationSpeed)
        {
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatDir.sqrMagnitude < 0.0001f) return;
            flatDir.Normalize();

            Quaternion targetRot = Quaternion.LookRotation(flatDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 按移动方向转身（供 Walk/Run 状态调用）。
        /// 瞄准时直接返回——朝向已由 UpdateFacing 锁定在相机方向，移动输入只产生 strafe 位移。
        /// </summary>
        public void RotateByMovement(Vector3 moveDirection)
        {
            if (IsAiming) return;
            if (moveDirection.sqrMagnitude > 0.01f)
                RotateToward(moveDirection);
        }

        /// <summary>
        /// 瞄准时把角色朝向锁定到相机水平方向（TPS strafe）。
        /// 因为 IInputProvider.MoveDirection 本身就是相机相对方向（PlayerInputProvider 按相机投影计算），
        /// 锁定朝向之后，输入的本地空间分解天然就是 左右(MoveX) / 前后(MoveY)，
        /// 正好驱动瞄准套的八方向 Blend Tree。
        /// 未瞄准时不干预——朝向由 Walk/Run 按移动方向决定。
        /// </summary>
        private void UpdateFacing()
        {
            if (!IsAiming || config == null) return;

            Vector3 look = inputProvider != null ? inputProvider.LookDirection : Vector3.zero;
            if (look.sqrMagnitude > 0.0001f)
                RotateToward(look, config.aimRotationSpeed);
        }

        /// <summary> 角色死亡——切换强状态并归零移动表现；Start 前收到死亡事件也不会驱动未初始化状态机 </summary>
        public void Die()
        {
            if (_started)
                fullBodySM?.ToIdle();
            _movementWasAllowed = false;
            coordinator?.ChangeState(CharacterState.Dead);
        }

        /// <summary> 角色复活；有 HealthController 时由 HealthReset 事件统一完成状态恢复 </summary>
        public void Respawn()
        {
            if (health != null)
                health.ResetHealth();
            else
                RestoreAfterHealthReset();
        }

        private void RestoreAfterHealthReset()
        {
            _autoAimRemain = 0f;
            IsAiming = false;
            coordinator?.ChangeState(CharacterState.Normal);
            if (!_started) return;

            fullBodySM?.ToIdle();
            _movementWasAllowed = true;
            upperBodySM?.SetMode(GetUpperBodyMode(equipmentCtrl?.CurrentWeapon));
            EquipInitialWeapons();
        }
    }
}
