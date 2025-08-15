using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DroneController
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance;

        [SerializeField] private InputActionReference _inputLeftJoyStick = default;
        [SerializeField] private InputActionReference _inputRightJoyStick = default;
        
        [SerializeField] private InputActionAsset _inputActionAsset = default;
        [SerializeField] private InputActionReference _inputPitch = default;
        [SerializeField] private InputActionReference _inputRoll = default;
        [SerializeField] private InputActionReference _inputYaw = default;
        [SerializeField] private InputActionReference _inputThrottle = default;
        [SerializeField] private InputActionReference _inputPressButtonA = default;
        [SerializeField] private InputActionReference _inputPressButtonB = default;
        [SerializeField] private InputActionReference _inputReturnHome = default;
        
        [SerializeField] private InputActionReference _inputLeftTriggerButton = default;
        [SerializeField] private InputActionReference _inputRightTriggerButton = default;
        
        [SerializeField] private InputActionReference _inputLeftGripButton = default;
        [SerializeField] private InputActionReference _inputRightGripButton = default;
        
        [SerializeField] private float _pitchInput = default;
        [SerializeField] private float _rollInput = default;
        [SerializeField] private float _yawInput = default;
        [SerializeField] private float _throttleInput = default;
        
        [SerializeField] private Vector2 _leftJoyStickInput = default;
        [SerializeField] private Vector2 _rightJoyStickInput = default;
        
        [SerializeField] private float _leftTriggerInput = default;
        [SerializeField] private float _rightTriggerInput = default;
        
        //Editor cheat
        [SerializeField] private InputActionReference _inputPressKeyboard_S = default;
        
        public float LeftTriggerInput
        {
            get { return _leftTriggerInput; }
        }
        
        public float RightTriggerInput
        {
            get { return _rightTriggerInput; }
        }
        
        public float PitchInput
        {
            get { return _pitchInput; }
            set { _pitchInput = value; }
        }

        public float RollInput
        {
            get { return _rollInput; }
            set { _rollInput = value; }
        }

        public float YawInput
        {
            get { return _yawInput; }
            set { _yawInput = value; }
        }

        public float ThrottleInput
        {
            get { return _throttleInput; }
            set { _throttleInput = value; }
        }
        
        public Vector2 LeftJoyStickInput
        {
            get { return _leftJoyStickInput; }
        }
        
        public Vector2 RightJoyStickInput
        {
            get { return _rightJoyStickInput; }
        }
        
        public delegate void OnPressButtonA();
        public static event OnPressButtonA onPressButtonA;
        
        public delegate void OnPressButtonB();
        public static event OnPressButtonB onPressButtonB;
        
        public delegate void OnReturnHomeAction();
        public static event OnReturnHomeAction OnReturnHomePressed;
        
        public delegate void OnPressKeyboard_S();
        public static event OnPressKeyboard_S onPressKeyboard_S;
        
        public delegate void OnGripPress(bool isPressed, bool isLeft);
        public static event OnGripPress onGripPress;

        private void Awake()
        {
            if (InputManager.Instance == null)
            {
                Instance = this;
            }
            else if (InputManager.Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            _inputActionAsset.Enable();

            //common input
            _inputLeftJoyStick.action.canceled += OnLeftInputChanged;
            _inputLeftJoyStick.action.performed += OnLeftInputChanged;
            
            _inputRightJoyStick.action.canceled += OnRightInputChanged;
            _inputRightJoyStick.action.performed += OnRightInputChanged;
            
            //specials input
            _inputPitch.action.canceled += OnPitchInputChanged;
            _inputPitch.action.performed += OnPitchInputChanged;
            _inputPitch.action.started += OnPitchInputChanged;

            _inputRoll.action.canceled += OnRollInputChanged;
            _inputRoll.action.performed += OnRollInputChanged;
            _inputRoll.action.started += OnRollInputChanged;

            _inputYaw.action.canceled += OnYawInputChanged;
            _inputYaw.action.performed += OnYawInputChanged;
            _inputYaw.action.started += OnYawInputChanged;

            _inputThrottle.action.canceled += OnThrottleInputChanged;
            _inputThrottle.action.performed += OnThrottleInputChanged;
            _inputThrottle.action.started += OnThrottleInputChanged;

            _inputPressButtonA.action.performed += OnInputPressButtonA;
            _inputPressButtonB.action.performed += OnInputPressButtonB;
            _inputReturnHome.action.performed += OnReturnHomePressedHandler;
            _inputPressKeyboard_S.action.performed += OnInputPressKeyboard_S;
            
            _inputLeftTriggerButton.action.performed += OnInputPressLeftTriggerButton;
            _inputLeftTriggerButton.action.canceled += OnInputPressLeftTriggerButton;
            
            _inputRightTriggerButton.action.performed += OnInputPressRightTriggerButton;
            _inputRightTriggerButton.action.canceled += OnInputPressRightTriggerButton;
            
            _inputLeftGripButton.action.performed += OnLeftGripPressed;
            _inputLeftGripButton.action.canceled += OnLeftGripReleased;
            _inputRightGripButton.action.performed += OnRightGripPressed;
            _inputRightGripButton.action.canceled += OnRightGripReleased;
            
        }

        private void OnDisable()
        {
            _inputPitch.action.canceled -= OnPitchInputChanged;
            _inputPitch.action.performed -= OnPitchInputChanged;
            _inputPitch.action.started -= OnPitchInputChanged;

            _inputRoll.action.canceled -= OnRollInputChanged;
            _inputRoll.action.performed -= OnRollInputChanged;
            _inputRoll.action.started -= OnRollInputChanged;

            _inputYaw.action.canceled -= OnYawInputChanged;
            _inputYaw.action.performed -= OnYawInputChanged;
            _inputYaw.action.started -= OnYawInputChanged;

            _inputThrottle.action.canceled -= OnThrottleInputChanged;
            _inputThrottle.action.performed -= OnThrottleInputChanged;
            _inputThrottle.action.started -= OnThrottleInputChanged;

            _inputPressButtonA.action.performed -= OnInputPressButtonA;
            _inputPressButtonB.action.performed -= OnInputPressButtonB;
            _inputReturnHome.action.performed -= OnReturnHomePressedHandler;
            _inputPressKeyboard_S.action.performed -= OnInputPressKeyboard_S;
            
            _inputLeftTriggerButton.action.performed -= OnInputPressLeftTriggerButton;
            _inputLeftTriggerButton.action.canceled -= OnInputPressLeftTriggerButton;
            
            _inputRightTriggerButton.action.performed -= OnInputPressRightTriggerButton;
            _inputRightTriggerButton.action.canceled -= OnInputPressRightTriggerButton;
            
            _inputLeftGripButton.action.performed -= OnLeftGripPressed;
            _inputLeftGripButton.action.canceled -= OnLeftGripReleased;
            _inputRightGripButton.action.performed -= OnRightGripPressed;
            _inputRightGripButton.action.canceled -= OnRightGripReleased;
            
            _inputActionAsset.Disable();
        }
        
        public bool IsInputIdle()
        {
            return Mathf.Approximately(_pitchInput, 0f) && Mathf.Approximately(_rollInput, 0f) &&
                   Mathf.Approximately(_throttleInput, 0f);
        }

        private void SetInputValue(ref float axis, float value)
        {
            axis = value;
        }

        private void OnInputPressRightTriggerButton(InputAction.CallbackContext obj)
        {
            SetInputValue(ref _leftTriggerInput, obj.ReadValue<float>());
        }

        private void OnInputPressLeftTriggerButton(InputAction.CallbackContext obj)
        {
            SetInputValue(ref _rightTriggerInput, obj.ReadValue<float>());
        }
        
        private void OnPitchInputChanged(InputAction.CallbackContext eventData)
        {
            SetInputValue(ref _pitchInput, eventData.ReadValue<float>());
        }

        private void OnRollInputChanged(InputAction.CallbackContext eventData)
        {
            SetInputValue(ref _rollInput, eventData.ReadValue<float>());
        }

        private void OnYawInputChanged(InputAction.CallbackContext eventData)
        {
            SetInputValue(ref _yawInput, eventData.ReadValue<float>());
        }

        private void OnThrottleInputChanged(InputAction.CallbackContext eventData)
        {
            SetInputValue(ref _throttleInput, eventData.ReadValue<float>());
        }
        
        private void OnInputPressButtonA(InputAction.CallbackContext context)
        {
            onPressButtonA?.Invoke();
        }
        
        private void OnInputPressButtonB(InputAction.CallbackContext context)
        {
            onPressButtonB?.Invoke();
        }
        
        private void OnReturnHomePressedHandler(InputAction.CallbackContext context)
        {
            OnReturnHomePressed?.Invoke();
        }
        
        private void OnInputPressKeyboard_S(InputAction.CallbackContext context)
        {
            onPressKeyboard_S?.Invoke();
        }
        
        private void OnLeftInputChanged(InputAction.CallbackContext eventData)
        {
            _leftJoyStickInput = eventData.ReadValue<Vector2>();
        }
        
        private void OnRightInputChanged(InputAction.CallbackContext eventData)
        {
            _rightJoyStickInput = eventData.ReadValue<Vector2>();
        }
        
        void OnLeftGripPressed(InputAction.CallbackContext ctx)
        {
            onGripPress?.Invoke(true, true);
        }
        void OnRightGripPressed(InputAction.CallbackContext ctx)
        {
            onGripPress?.Invoke(true, false);
        }

        void OnLeftGripReleased(InputAction.CallbackContext ctx)
        {
            onGripPress?.Invoke(false, true);
        }
        void OnRightGripReleased(InputAction.CallbackContext ctx)
        {
            onGripPress?.Invoke(false, false);
        }
    }
}