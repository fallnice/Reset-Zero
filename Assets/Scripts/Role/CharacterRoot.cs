using UnityEngine;
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
    public class CharacterRoot : MonoBehaviour
    {
        // ===== 输入 =====
        // IInputProvider 是接口，Inspector 无法序列化，Awake 中自动获取
        public IInputProvider inputProvider { get; private set; }

        /// <summary> 角色 Animator（自动获取子级） </summary>
        public Animator Animator { get; private set; }

        [Header("配置")]
        [SerializeField] private CharacterConfig config;
        /// <summary> 角色配置数据（速度/重力/转身等），必须拖入 </summary>
        public CharacterConfig Config => config;

        [Header("协调器")]
        [SerializeField] private CharacterStateCoordinator coordinator;

        // 状态机是纯 C# 类，不挂 GameObject，Awake 中 new
        public StateMachine.FullBodyStateMachine fullBodySM;
        public StateMachine.UpperBodyStateMachine upperBodySM;
        private StateMachine.IUpperBodyAnimationSink _upperBodyAnimationSink;

        [Header("子控制器（可选，未拖拽则自动查找）")]
        [SerializeField] private Controllers.EquipmentController equipmentCtrl;
        [SerializeField] private Controllers.IKController ikCtrl;
        [SerializeField] private Controllers.ExpressionController expressionCtrl;
        [SerializeField] private Controllers.AudioController audioCtrl;

        // ===== Unity 生命周期 =====

        private void Awake()
        {
            // 获取输入提供者（PlayerInputProvider 实现了 IInputProvider）
            inputProvider = GetComponentInChildren<IInputProvider>();

            // 获取 Animator 与可选的上半身动画适配器（子级模型上）
            Animator = GetComponentInChildren<Animator>();
            _upperBodyAnimationSink = GetComponentInChildren<StateMachine.IUpperBodyAnimationSink>();

            // 获取或自动添加协调器
            if (coordinator == null)
                coordinator = GetComponentInChildren<CharacterStateCoordinator>();
            if (coordinator == null)
                coordinator = gameObject.AddComponent<CharacterStateCoordinator>();

            // 创建状态机（纯 C# 类，不是 MonoBehaviour）
            fullBodySM = new StateMachine.FullBodyStateMachine();
            upperBodySM = new StateMachine.UpperBodyStateMachine();

            // 获取子控制器（全部可选，缺失只 log 不报错）
            if (equipmentCtrl == null)
                equipmentCtrl = GetComponentInChildren<Controllers.EquipmentController>();
            if (ikCtrl == null)
                ikCtrl = GetComponentInChildren<Controllers.IKController>();
            if (expressionCtrl == null)
                expressionCtrl = GetComponentInChildren<Controllers.ExpressionController>();
            if (audioCtrl == null)
                audioCtrl = GetComponentInChildren<Controllers.AudioController>();

            // 由根节点显式注入依赖，避免子控制器 Awake 顺序不确定
            if (equipmentCtrl != null)
            {
                equipmentCtrl.Init(this, coordinator);
                equipmentCtrl.WeaponEquipped += HandleWeaponEquipped;
                equipmentCtrl.AttackCommitted += HandleAttackCommitted;
            }

            // 检查 CharacterController
            if (GetComponent<CharacterController>() == null)
                Debug.LogError("[CharacterRoot] 缺少 CharacterController 组件，角色无法移动", this);

            // 检查 Config
            if (config == null)
                Debug.LogError("[CharacterRoot] 未分配 CharacterConfig，请在 Inspector 拖入", this);
        }

        private void Start()
        {
            // 注册子控制器到协调器
            if (coordinator != null)
            {
                if (equipmentCtrl != null) coordinator.Register(equipmentCtrl);
                if (ikCtrl != null) coordinator.Register(ikCtrl);
                if (expressionCtrl != null) coordinator.Register(expressionCtrl);
                if (audioCtrl != null) coordinator.Register(audioCtrl);
            }

            // 初始化状态机（注入 character + coordinator；动画 Sink 可为空）
            fullBodySM.Init(this, coordinator);
            upperBodySM.Init(this, coordinator, _upperBodyAnimationSink);

            // 设置初始状态，并同步可能在 Start 前已装备的武器
            fullBodySM.ToIdle();
            upperBodySM.SetMode(GetUpperBodyMode(equipmentCtrl?.CurrentWeapon));
        }

        private void Update()
        {
            bool canAttack = coordinator == null || coordinator.CanAttack;
            upperBodySM.SetSuppressed(!canAttack);
            HandleCombatInput();

            // 死亡/眩晕/过场等状态下停止移动状态机
            bool canMove = coordinator == null || coordinator.CanMove;
            if (canMove)
            {
                fullBodySM.OnUpdate();
            }

            // 抑制只关闭叠加表现，请求姿态保留，恢复后自动同步
            upperBodySM.OnUpdate();
        }

        /// <summary> 将抽象输入命令路由给装备模块；槽位切换优先于同帧攻击 </summary>
        private void HandleCombatInput()
        {
            if (inputProvider == null || equipmentCtrl == null) return;

            if (inputProvider.SelectPrimaryPressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Primary);
            else if (inputProvider.SelectSecondaryPressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Secondary);
            else if (inputProvider.SelectMeleePressedThisFrame)
                equipmentCtrl.SwitchTo(WeaponSlot.Melee);

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
        }

        /// <summary> 根据武器玩法类型选择持续上半身姿态，不依赖 Animator 参数 </summary>
        private static StateMachine.UpperBodyMode GetUpperBodyMode(WeaponConfig weapon)
        {
            return weapon != null && weapon.type == WeaponType.Ranged
                ? StateMachine.UpperBodyMode.RangedReady
                : StateMachine.UpperBodyMode.Inactive;
        }

        private void LateUpdate()
        {
            // IK 必须在 Animator 之后执行
            ikCtrl?.OnLateUpdate();
        }

        /// <summary>
        /// 接管 Animator 的 Root Motion 控制权——空实现 = 动画自带位移被丢弃，所有位移由代码驱动
        /// 这样即使 Animator 的 Apply Root Motion 勾着，动画也不会抢 CharacterController 的位移
        /// </summary>
        private void OnAnimatorMove() { }

        private void OnDestroy()
        {
            if (equipmentCtrl != null)
            {
                equipmentCtrl.WeaponEquipped -= HandleWeaponEquipped;
                equipmentCtrl.AttackCommitted -= HandleAttackCommitted;
            }

            Blackboard.Clear();
        }

        // ===== 公共接口 =====

        /// <summary>
        /// 平滑转向目标方向（只取水平面，保持角色不抬头/低头）
        /// </summary>
        public void RotateToward(Vector3 direction)
        {
            if (config == null) return;

            Vector3 flatDir = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatDir.sqrMagnitude < 0.0001f) return;
            flatDir.Normalize();

            Quaternion targetRot = Quaternion.LookRotation(flatDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, config.rotationSpeed * Time.deltaTime);
        }

        /// <summary> 角色死亡——只改状态，各控制器自行响应 </summary>
        public void Die()
        {
            coordinator?.ChangeState(CharacterState.Dead);
        }

        /// <summary> 角色复活 </summary>
        public void Respawn()
        {
            coordinator?.ChangeState(CharacterState.Normal);
        }
    }
}
