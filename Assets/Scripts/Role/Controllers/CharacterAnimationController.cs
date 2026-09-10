using Combat;
using Role.Core;
using Role.StateMachine;
using UnityEngine;

namespace Role.Controllers
{
    /// <summary>
    /// 角色动画表现层——把逻辑层事件翻译成 Animator 调用，并负责武器模型挂载。
    ///
    /// 设计意图：玩法逻辑（装备切换、攻击冷却、伤害结算）与表现完全解耦。
    /// 本组件只做「事件/语义 → Animator 参数」的映射，不含任何玩法判断；
    /// 缺失动画或 Animator 时逻辑层照常运行（所有调用均判空）。
    ///
    /// 上半身语义由 UpperBodyStateMachine 驱动：本组件实现 IUpperBodyAnimationSink，
    /// 被 CharacterRoot.Awake 通过 GetComponentInChildren 自动发现并注入。
    ///
    /// 需要的 Animator 参数：
    ///   Attack (Trigger)      攻击（近战集表现挥砍，枪械集表现射击）
    ///   Die (Trigger)         死亡
    ///   Hit (Trigger)         受击
    ///   MoveX / MoveY (Float) 仅枪械动画集需要：本地空间移动方向，驱动八方向 2D Blend Tree。
    ///                         近战集沿用 FullBody 状态机已有的 Speed / IsGrounded，本组件不写。
    ///   IsAiming (Bool)       仅枪械动画集需要：true = 瞄准套（按住瞄准键，或远程开火后的自动保持期）。
    ///
    /// 动画集分三层：近战 / 枪械未瞄准 / 枪械瞄准。
    /// 「近战 ↔ 枪械」用两个独立 RuntimeAnimatorController 整体切换（Blend Tree 维度不同，结构不兼容）；
    /// 「枪械未瞄准 ↔ 枪械瞄准」**必须在同一个枪械 controller 内部用 IsAiming 切子状态机**——
    /// 整体替换 runtimeAnimatorController 会重置状态机，按住/松开瞄准键时会把攻击、受击动画打断。
    /// </summary>
    public class CharacterAnimationController : MonoBehaviour, IUpperBodyAnimationSink
    {
        [Header("动画")]
        [SerializeField] private Animator animator;           // 留空则取 CharacterRoot.Animator

        [Header("默认动画控制器（按武器类型回退；留空则不切换）")]
        [Tooltip("武器自身的 WeaponConfig.animatorController 优先，这里只是没配时的兜底")]
        [SerializeField] private RuntimeAnimatorController meleeController;  // 近战：1D Blend Tree（Speed）
        [SerializeField] private RuntimeAnimatorController rangedController; // 枪械：2D 八方向（MoveX/MoveY）

        [Header("武器挂载")]
        [SerializeField] private Transform weaponAttachPoint; // 留空则用 EquipmentController 的右手挂点

        // Animator 参数名哈希（避免每帧字符串查找）
        private static readonly int HashAttack = Animator.StringToHash("Attack");
        private static readonly int HashDie = Animator.StringToHash("Die");
        private static readonly int HashHit = Animator.StringToHash("Hit");
        private static readonly int HashMoveX = Animator.StringToHash("MoveX");
        private static readonly int HashMoveY = Animator.StringToHash("MoveY");
        private static readonly int HashIsAiming = Animator.StringToHash("IsAiming");
        private static readonly int HashPutWeapon = Animator.StringToHash("PutWeapon");
        private static readonly int HashTakeWeapon = Animator.StringToHash("TakeWeapon");
        private static readonly int HashEquipSpeed = Animator.StringToHash("EquipSpeed");

        // 换装剪辑的匹配关键字：两套动画集命名不同（近战 TK_Take / 枪械 TakeGun），只能按关键字找
        private const string TakeClipKeyword = "Take";
        private const string PutClipKeyword = "Put";

        // 换装动画的播放速度区间：剪辑长度异常时兜底，避免动作被拉到鬼畜或几乎不动
        private const float MIN_EQUIP_SPEED = 0.5f;
        private const float MAX_EQUIP_SPEED = 6f;

        /// <summary>
        /// 最近一次下发的上半身姿态。换 controller 会把 Animator 参数重置回默认值，
        /// 故切完动画集必须重发一次，否则「持枪瞄准中切武器」会丢掉瞄准套。
        /// </summary>
        private UpperBodyMode _currentMode = UpperBodyMode.Inactive;

        private CharacterRoot _character;
        private EquipmentController _equipment;
        private HealthController _health;
        private GameObject _weaponModel;

        /// <summary>
        /// 当前动画集是否带换装参数（PutWeapon / TakeWeapon / EquipSpeed）。
        /// 换 controller 后重算：对不存在的参数调用 SetTrigger 会往 Console 刷告警。
        /// </summary>
        private bool _hasEquipParameters;

        private void Awake()
        {
            _character = GetComponent<CharacterRoot>();
        }

        // 依赖在 Start 获取：确保所有 Awake 完成（EquipmentController.Init 由 CharacterRoot.Awake 调用），
        // 避免脚本 Awake 顺序不确定导致拿到 null
        private void Start()
        {
            if (_character == null) return;

            _equipment = _character.Equipment;
            _health = _character.Health;

            if (animator == null) animator = _character.Animator;
            if (weaponAttachPoint == null && _equipment != null)
                weaponAttachPoint = _equipment.RightHandAttachPoint;

            // Start 执行顺序不确定：CharacterRoot 可能已下发过姿态，这里补一次保证参数与缓存一致
            WriteAimParameter();

            if (_equipment != null)
            {
                _equipment.WeaponEquipped += HandleWeaponEquipped;
                _equipment.AttackCommitted += HandleAttackCommitted;
                _equipment.UnequipStarted += HandleUnequipStarted;

                // 同步 Start 之前就已装备的武器（如调试菜单/敌人初始武器/出生自带双刀）
                ApplyAnimationSet(_equipment.CurrentWeapon);
                RefreshWeaponModel(_equipment.CurrentWeapon);
            }

            if (_health != null)
            {
                _health.Damaged += HandleDamaged;
                _health.Died += HandleDied;
            }
        }

        /// <summary>
        /// 枪械动画集用 2D 八方向 Blend Tree，需每帧写入本地空间移动方向。
        /// 近战动画集走 1D（Speed / IsGrounded，由 FullBody 状态机写入），此处不写，
        /// 避免对不存在的参数调用 SetFloat 产生告警。
        /// </summary>
        private void Update()
        {
            if (animator == null || _equipment == null) return;

            WeaponConfig weapon = _equipment.CurrentWeapon;
            if (weapon == null || weapon.type != WeaponType.Ranged) return;

            IInputProvider input = _character != null ? _character.inputProvider : null;
            Vector3 worldDir = input != null ? input.MoveDirection : Vector3.zero;

            // 世界方向转角色本地空间：x = 左右，z = 前后，对应 2D Blend Tree 的 MoveX / MoveY
            Vector3 localDir = transform.InverseTransformDirection(worldDir);
            animator.SetFloat(HashMoveX, localDir.x);
            animator.SetFloat(HashMoveY, localDir.z);
        }

        private void OnDestroy()
        {
            if (_equipment != null)
            {
                _equipment.WeaponEquipped -= HandleWeaponEquipped;
                _equipment.AttackCommitted -= HandleAttackCommitted;
                _equipment.UnequipStarted -= HandleUnequipStarted;
            }

            if (_health != null)
            {
                _health.Damaged -= HandleDamaged;
                _health.Died -= HandleDied;
            }
        }

        // ===== 逻辑事件 → 动画 =====

        /// <summary>
        /// 换装第一段：逻辑层开始收起旧武器（此时动画集还没换）。
        /// 表现层在**旧**动画集上播「收武器」，等逻辑层的 putDuration 走完才落位。
        /// </summary>
        private void HandleUnequipStarted(WeaponConfig oldWeapon)
        {
            if (oldWeapon == null || _equipment == null) return;
            PlayEquipAnimation(HashPutWeapon, PutClipKeyword, _equipment.PutDuration);
        }

        /// <summary>武器切换完成：切换动画集 + 刷新手持模型 + 播换装第二段（掏出新武器）</summary>
        private void HandleWeaponEquipped(WeaponConfig oldWeapon, WeaponConfig newWeapon)
        {
            ApplyAnimationSet(newWeapon);
            RefreshWeaponModel(newWeapon);

            // 必须在 ApplyAnimationSet 之后触发：换 runtimeAnimatorController 会重置 Animator 参数，
            // 先设的 Trigger 会被清掉。空手（三槽全空）时没有武器可掏，直接跳过。
            if (newWeapon != null && _equipment != null && _equipment.TakeDuration > 0f)
                PlayEquipAnimation(HashTakeWeapon, TakeClipKeyword, _equipment.TakeDuration);
        }

        // ===== 换装动画 =====

        /// <summary>
        /// 播一次换装动画（收武器 / 掏出武器）。
        ///
        /// 播放速度按「剪辑时长 ÷ 逻辑时长」对齐：动画恰好演完时逻辑层才解锁攻击。
        /// 不对齐的话会出现两种错帧——动画还没演完就能开枪，或者攻击 Trigger 在 Take 状态里
        /// 排队（Take 没有 Attack 转出），等回到待机才突然挥一刀。
        /// 目标时长为 0 或找不到剪辑时不改速度，退回动画集内置的 1 倍速。
        /// </summary>
        private void PlayEquipAnimation(int triggerHash, string clipKeyword, float targetDuration)
        {
            if (animator == null || !_hasEquipParameters) return;

            float speed = 1f;
            if (targetDuration > 0f)
            {
                float clipLength = FindClipLength(clipKeyword);
                if (clipLength > 0f)
                    speed = Mathf.Clamp(clipLength / targetDuration, MIN_EQUIP_SPEED, MAX_EQUIP_SPEED);
            }

            animator.SetFloat(HashEquipSpeed, speed);
            animator.SetTrigger(triggerHash);
        }

        /// <summary> 在当前动画集里按关键字找换装剪辑时长；找不到返回 0（调用方退回 1 倍速） </summary>
        private float FindClipLength(string clipKeyword)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null) return 0f;

            AnimationClip[] clips = controller.animationClips;
            if (clips == null) return 0f;

            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null) continue;
                if (clip.name.IndexOf(clipKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip.length;
            }

            return 0f;
        }

        /// <summary> 当前动画集是否带换装参数；仅在换 controller 时调用（animator.parameters 会分配数组） </summary>
        private bool HasEquipParameters()
        {
            if (animator == null) return false;

            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters == null) return false;

            bool hasPut = false;
            bool hasTake = false;
            bool hasSpeed = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                int hash = parameters[i].nameHash;
                if (hash == HashPutWeapon) hasPut = true;
                else if (hash == HashTakeWeapon) hasTake = true;
                else if (hash == HashEquipSpeed) hasSpeed = true;
            }

            return hasPut && hasTake && hasSpeed;
        }

        /// <summary>
        /// 按武器类型整体切换动画集（近战一套 / 枪械一套）。
        /// 两者共用同一套状态机结构与参数名，只替换 clip，因此切换后 Attack/Fire/Die/Hit 等调用不受影响。
        /// 注意：切换 runtimeAnimatorController 会重置状态机，故仅在目标不同时才切，避免无谓重置。
        /// </summary>
        private void ApplyAnimationSet(WeaponConfig weapon)
        {
            if (animator == null) return;

            RuntimeAnimatorController target = ResolveController(weapon);

            // 未配置对应的动画集时保持现状，兼容只做了一套动画的情况；
            // 目标相同时不切，避免无谓的状态机重置
            if (target != null && animator.runtimeAnimatorController != target)
                animator.runtimeAnimatorController = target;

            // 参数构成随 controller 变，重新探测一次换装参数是否存在
            _hasEquipParameters = HasEquipParameters();

            // 换 controller 会重置 Animator 参数，故切完重发一次瞄准参数
            WriteAimParameter();
        }

        /// <summary>
        /// 决定一把武器用哪套动画集：武器自身配置优先（同一类型可有多把武器各配一套），
        /// 未配置时按 type 回退到 Inspector 上的默认集。
        ///
        /// 空手（未装备 / 刚丢弃）回退到近战集，而不是返回 null——返回 null 会被上层当作
        /// 「没配置动画集，保持现状」，于是丢枪后一直停在枪械集上，空手也播持枪动作。
        /// </summary>
        private RuntimeAnimatorController ResolveController(WeaponConfig weapon)
        {
            if (weapon == null) return meleeController;
            if (weapon.animatorController != null) return weapon.animatorController;

            return weapon.type == WeaponType.Ranged ? rangedController : meleeController;
        }

        /// <summary>
        /// 攻击提交：近战与远程统一走 Attack 状态。
        /// 具体播什么由动画集决定——近战集把 Attack 映射为挥砍，枪械集映射为射击，
        /// 因此无需为远程单独建 Fire 状态。
        /// </summary>
        private void HandleAttackCommitted(WeaponConfig weapon)
        {
            if (weapon == null) return;
            animator?.SetTrigger(HashAttack);
        }

        private void HandleDamaged(float remainingHealth)
        {
            animator?.SetTrigger(HashHit);
        }

        private void HandleDied()
        {
            animator?.SetTrigger(HashDie);
        }

        // ===== 武器模型 =====

        private void RefreshWeaponModel(WeaponConfig weapon)
        {
            if (_weaponModel != null)
            {
                Destroy(_weaponModel);
                _weaponModel = null;
            }

            // 未配置模型或挂点时静默跳过：逻辑层仍可正常攻击
            if (weapon == null || weapon.modelPrefab == null || weaponAttachPoint == null) return;

            _weaponModel = Instantiate(weapon.modelPrefab, weaponAttachPoint);

            // 只归零位置，旋转与缩放保留预制体自身的设定：武器模型是靠节点自己的旋转摆正的
            // （Weapon_Rifle 根节点是 identity，Weapon_Katana_mini_R 根节点带 180° 翻转），
            // 强行清成 identity 会让刀躺倒/反手。
            _weaponModel.transform.localPosition = Vector3.zero;
        }

        // ===== IUpperBodyAnimationSink =====

        /// <summary>
        /// 上半身持续姿态 → Animator 参数。
        /// 持刀 / 持枪（未瞄准）之间的区别由整套动画集切换表现，不需要参数；
        /// 只有「枪械瞄准套 ↔ 未瞄准套」这一层要在枪械 controller 内部用 IsAiming 切子状态机。
        /// </summary>
        public void SetUpperBodyMode(UpperBodyMode mode)
        {
            _currentMode = mode;
            WriteAimParameter();
        }

        /// <summary> 写入瞄准参数；切动画集后也要调用——参数会随 controller 切换被重置 </summary>
        private void WriteAimParameter()
        {
            animator?.SetBool(HashIsAiming, _currentMode == UpperBodyMode.RangedAiming);
        }

        /// <summary>
        /// 开火同样由动画集的 Attack 状态表现（枪械集已把 Attack 映射为射击动画），
        /// 此处不单独触发 Fire，避免与 Attack 重复触发。
        /// </summary>
        public void PlayUpperBodyAction(UpperBodyAction action) { }

        // ===== 资产包动画事件 =====

        /// <summary> 左手 IK 扶枪请求——由瞄准类剪辑的动画事件置位，供手部 IK 层消费 </summary>
        public bool IsLeftHandIkRequested => _isLeftHandIkRequested;

        // 命令字面量来自 CombatGirls 资产包剪辑内的动画事件参数，不能改名，故集中在此
        private const string CommandToRightHand = "To_Hand_R_Socket";         // 武器切到右手插槽
        private const string CommandIkOnLeftHandle = "IK_ON_Left_Handle";     // 左手 IK 扶枪：开
        private const string CommandIkOffLeftHandle = "IK_OFF_Left_Handle";   // 左手 IK 扶枪：关

        private bool _isLeftHandIkRequested;

        /// <summary>
        /// CombatGirls 资产包在瞄准 / 换枪 / 待机等剪辑里刻了动画事件 `SwitchSocket`，
        /// 参数是逗号分隔的命令串（如 `To_Hand_R_Socket, IK_ON_Left_Handle`）。
        ///
        /// 方法名与签名由资产决定、不可改：Unity 按「方法名 + 参数类型」在 **Animator 所在 GameObject**
        /// 上查找接收者，找不到就对每次事件刷 "AnimationEvent 'SwitchSocket' ... has no receiver"。
        /// 本组件恰好与 Animator 同物体，故在此接住（签名与资产包 Character_Weapon_Controller 一致，
        /// 顺带能拿到触发它的剪辑名）。资源包里 43 个剪辑带这个事件，不接住会持续污染 Console。
        /// </summary>
        public void SwitchSocket(AnimationEvent animEvent)
        {
            if (animEvent == null) return;
            SwitchSocketByString(animEvent.stringParameter);
        }

        /// <summary>
        /// 字符串入口：与资产包 Character_Weapon_Controller 的同名方法对齐，
        /// 便于 StateMachineBehaviour 或脚本直接调用（不经过动画事件）。
        /// </summary>
        public void SwitchSocketByString(string commands)
        {
            if (string.IsNullOrEmpty(commands)) return;

            // 手写拆分而不用 string.Split：瞄准移动时多个剪辑同时在跑，事件会随剪辑循环反复触发，
            // Split 每次都分配数组与子串，按项目零 GC 红线要求避开
            int cursor = 0;
            while (cursor <= commands.Length)
            {
                int comma = commands.IndexOf(',', cursor);
                int end = comma < 0 ? commands.Length : comma;

                DispatchSocketCommand(commands, cursor, end);

                if (comma < 0) break;
                cursor = comma + 1;
            }
        }

        /// <summary>
        /// 单条命令分发。资产包原语义（见 Rifle_Full_Body.prefab 的 Character_Weapon_Controller）是
        /// 用 ParentConstraint 在 Hand_R_Socket / Put_Socket_Rifle / add_weapon_r 之间切权重；
        /// 我们不做多插槽约束（武器模型常驻右手挂点），故只认与实际表现相关的命令，其余静默忽略。
        /// </summary>
        private void DispatchSocketCommand(string source, int start, int end)
        {
            // 资产包的事件参数带多余空格（YAML 里就写成了 'To_Hand_R_Socket, IK_ON_Left_Handle  '），
            // 先掐掉首尾空白再比对，避免为此分配一个 trim 后的新字符串
            while (start < end && char.IsWhiteSpace(source[start])) start++;
            while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
            if (end <= start) return;

            if (CommandEquals(source, start, end, CommandIkOnLeftHandle))
            {
                _isLeftHandIkRequested = true;
                return;
            }

            if (CommandEquals(source, start, end, CommandIkOffLeftHandle))
            {
                _isLeftHandIkRequested = false;
                return;
            }

            if (CommandEquals(source, start, end, CommandToRightHand))
            {
                // 武器模型本就常驻右手挂点，无需切换；这里显式列出，便于确认「命令已被识别」
                return;
            }

            // 其余命令（To_Put_Socket_Rifle / To_add_weapon_r 等收纳挂点）对应资产包的多插槽约束展示，
            // 我们没有对应系统，静默忽略。将来做「背到背后 / 收枪」时在此扩展即可。
        }

        /// <summary> 区间子串与期望命令的免分配、忽略大小写比较 </summary>
        private static bool CommandEquals(string source, int start, int end, string expected)
        {
            int length = end - start;
            if (length != expected.Length) return false;
            return string.Compare(source, start, expected, 0, length, System.StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
