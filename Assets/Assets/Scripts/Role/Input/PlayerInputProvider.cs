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
        private bool    _actionPressed;
        private bool    _interactPressedThisFrame;
        private bool    _sprintHeld;
        private bool    _openBagPressedThisFrame;     // 边缘触发：按下当帧有效
        private bool    _openCraftPressedThisFrame;
        private bool    _togglePanelPressedThisFrame;
        private bool    _closeAllPressedThisFrame;

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

        public bool JumpPressed  => _jumpPressedThisFrame;
        public bool IsJumpHeld   => _jumpHeld;
        public bool ActionPressed => _actionPressed;
        public bool InteractPressed => _interactPressedThisFrame;
        public bool SprintPressed => _sprintHeld;
        public bool HasAnyInput   => _moveInput.sqrMagnitude > 0.01f || _jumpPressedThisFrame || _actionPressed || _interactPressedThisFrame;

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
        }

        private void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        // 每帧结束时清除边缘触发标志
        private void LateUpdate()
        {
            _jumpPressedThisFrame = false;
            _interactPressedThisFrame = false;
            _openBagPressedThisFrame = false;
            _openCraftPressedThisFrame = false;
            _togglePanelPressedThisFrame = false;
            _closeAllPressedThisFrame = false;
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

        public void OnPrimaryAction(InputAction.CallbackContext context)
        {
            // performed 阶段设为 true，canceled 阶段（松开）设为 false
            _actionPressed = context.phase == InputActionPhase.Performed;
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
    }
}
