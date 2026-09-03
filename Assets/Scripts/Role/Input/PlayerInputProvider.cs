using UnityEngine;
using UnityEngine.InputSystem;
using Role.Core;

namespace Role.Input
{
    /// <summary>
    /// 玩家输入提供者——实现生成的 IPlayerActions，桥接到 IInputProvider
    /// 挂载在角色 GameObject 上
    /// </summary>
    public class PlayerInputProvider : MonoBehaviour, IInputProvider, IUiInputProvider, PlayerInputActions.IPlayerActions
    {
        // ===== 输入缓存（由 InputAction 回调更新）=====

        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool    _jumpPressedThisFrame;   // 边缘触发：按下当帧有效
        private bool    _jumpHeld;               // 持续状态
        private bool    _attackPressedThisFrame;
        private bool    _attackHeld;
        private bool    _selectPrimaryPressedThisFrame;
        private bool    _selectSecondaryPressedThisFrame;
        private bool    _selectMeleePressedThisFrame;
        private bool    _interactPressedThisFrame;
        private bool    _sprintHeld;
        private bool    _openBagPressedThisFrame;     // 边缘触发：按下当帧有效
        private bool    _openCraftPressedThisFrame;
        private bool    _togglePanelPressedThisFrame;
        private bool    _closeAllPressedThisFrame;
        private bool    _dropWeaponPressedThisFrame;

        // ===== 生成的 Input Action 实例 =====
        private PlayerInputActions _inputActions;

        // ===== IInputProvider 实现 =====

        public Vector3 MoveDirection
        {
            get
            {
                var cam = Camera.main;
                if (cam == null)
                    return new Vector3(_moveInput.x, 0, _moveInput.y).normalized;

                Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                Vector3 right   = Vector3.ProjectOnPlane(cam.transform.right,   Vector3.up).normalized;
                return (forward * _moveInput.y + right * _moveInput.x).normalized;
            }
        }

        public Vector3 LookDirection
        {
            get
            {
                var cam = Camera.main;
                if (cam == null) return Vector3.forward;
                return cam.transform.forward;
            }
        }

        public Vector2 LookDelta => _lookInput;

        public bool JumpPressed => _jumpPressedThisFrame;
        public bool IsJumpHeld => _jumpHeld;
        public bool AttackPressedThisFrame => _attackPressedThisFrame;
        public bool AttackHeld => _attackHeld;
        public bool SelectPrimaryPressedThisFrame => _selectPrimaryPressedThisFrame;
        public bool SelectSecondaryPressedThisFrame => _selectSecondaryPressedThisFrame;
        public bool SelectMeleePressedThisFrame => _selectMeleePressedThisFrame;
        public bool InteractPressed => _interactPressedThisFrame;
        public bool SprintPressed => _sprintHeld;
        public bool DropWeaponPressedThisFrame => _dropWeaponPressedThisFrame;
        public bool HasAnyInput => _moveInput.sqrMagnitude > 0.01f
            || _jumpPressedThisFrame
            || _attackHeld
            || _interactPressedThisFrame
            || _selectPrimaryPressedThisFrame
            || _selectSecondaryPressedThisFrame
            || _selectMeleePressedThisFrame
            || _dropWeaponPressedThisFrame;

        // ===== IUiInputProvider 实现 =====

        public bool OpenBagPressed     => _openBagPressedThisFrame;
        public bool OpenCraftPressed   => _openCraftPressedThisFrame;
        public bool TogglePanelPressed => _togglePanelPressedThisFrame;
        public bool CloseAllPressed    => _closeAllPressedThisFrame;

        // ===== Unity 生命周期 =====

        private void Awake()
        {
            _inputActions = new PlayerInputActions();
        }

        private void OnEnable()
        {
            _inputActions.Player.AddCallbacks(this);
            _inputActions.Enable();
        }

        private void OnDisable()
        {
            _inputActions.Player.RemoveCallbacks(this);
            _inputActions.Disable();
            ResetInputCache();
        }

        private void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        // 每帧结束时清除边缘触发标志
        private void LateUpdate()
        {
            _jumpPressedThisFrame = false;
            _attackPressedThisFrame = false;
            _selectPrimaryPressedThisFrame = false;
            _selectSecondaryPressedThisFrame = false;
            _selectMeleePressedThisFrame = false;
            _interactPressedThisFrame = false;
            _openBagPressedThisFrame = false;
            _openCraftPressedThisFrame = false;
            _togglePanelPressedThisFrame = false;
            _closeAllPressedThisFrame = false;
            _dropWeaponPressedThisFrame = false;
        }

        /// <summary> 清空所有输入缓存，避免组件重新启用后继承旧输入 </summary>
        private void ResetInputCache()
        {
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
            _jumpPressedThisFrame = false;
            _jumpHeld = false;
            _attackPressedThisFrame = false;
            _attackHeld = false;
            _selectPrimaryPressedThisFrame = false;
            _selectSecondaryPressedThisFrame = false;
            _selectMeleePressedThisFrame = false;
            _interactPressedThisFrame = false;
            _sprintHeld = false;
            _openBagPressedThisFrame = false;
            _openCraftPressedThisFrame = false;
            _togglePanelPressedThisFrame = false;
            _closeAllPressedThisFrame = false;
            _dropWeaponPressedThisFrame = false;
        }

        // ===== IPlayerActions 回调（对接生成的 PlayerInputActions）=====

        public void OnMove(InputAction.CallbackContext context)
        {
            _moveInput = context.ReadValue<Vector2>();
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            _lookInput = context.ReadValue<Vector2>();
        }

        public void OnJump(InputAction.CallbackContext context)
        {
            if (context.performed)
                _jumpPressedThisFrame = true;
            _jumpHeld = context.ReadValueAsButton();
        }

        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.performed)
                _attackPressedThisFrame = true;
            _attackHeld = context.ReadValueAsButton();
        }

        public void OnSelectPrimary(InputAction.CallbackContext context)
        {
            if (context.performed)
                _selectPrimaryPressedThisFrame = true;
        }

        public void OnSelectSecondary(InputAction.CallbackContext context)
        {
            if (context.performed)
                _selectSecondaryPressedThisFrame = true;
        }

        public void OnSelectMelee(InputAction.CallbackContext context)
        {
            if (context.performed)
                _selectMeleePressedThisFrame = true;
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            _sprintHeld = context.ReadValueAsButton();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            if (context.performed)
                _interactPressedThisFrame = true;
        }

        public void OnOpenBag(InputAction.CallbackContext context)
        {
            if (context.performed)
                _openBagPressedThisFrame = true;
        }

        public void OnOpenCraft(InputAction.CallbackContext context)
        {
            if (context.performed)
                _openCraftPressedThisFrame = true;
        }

        public void OnTogglePanel(InputAction.CallbackContext context)
        {
            if (context.performed)
                _togglePanelPressedThisFrame = true;
        }

        public void OnCloseAll(InputAction.CallbackContext context)
        {
            if (context.performed)
                _closeAllPressedThisFrame = true;
        }

        public void OnDropWeapon(InputAction.CallbackContext context)
        {
            if (context.performed)
                _dropWeaponPressedThisFrame = true;
        }
    }
}
