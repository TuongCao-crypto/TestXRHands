using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine;

namespace DroneController
{
    public enum DroneState
    {
        /// <summary>Drone is powered off.</summary>
        Off = 0,

        /// <summary>Drone is starting its engine.</summary>
        StartingEngine,

        /// <summary>Drone is ready for flying.</summary>
        ReadyTOFlying,

        /// <summary>Drone is actively flying.</summary>
        Flying,

        /// <summary>Drone is returning to its home position.</summary>
        ReturnHome,

        HorverLanding,
        PrepareAutoLanding,

        /// <summary>Drone is performing auto-landing.</summary>
        AutoLanding
    }

    [RequireComponent(typeof(Rigidbody))]
    public class DroneMovement : MonoBehaviour
    {
        public delegate void OnStateChangedDelegate(DroneState newState);
        public event OnStateChangedDelegate OnStateChanged;
        
        private FlightInputChannel inputChannel;
        float ReadPitch()    => inputChannel ? inputChannel.Pitch    : InputManager.Instance.PitchInput;
        float ReadRoll()     => inputChannel ? inputChannel.Roll     : InputManager.Instance.RollInput;
        float ReadYaw()      => inputChannel ? inputChannel.Yaw      : InputManager.Instance.YawInput;
        float ReadThrottle() => inputChannel ? inputChannel.Throttle : InputManager.Instance.ThrottleInput;


        [Header("Project References:")] [SerializeField]
        private DroneMovementData _droneMovementData = default;

        [Header("Local References:")] [SerializeField]
        private Transform _droneObject = default;

        // Component renferences.
        private Rigidbody _rigidbody = default;

        // Calculation values.
        private Vector3 _smoothDampToStopVelocity = default;
        private float _currentRollAmount = default;
        private float _currentRollAmountVelocity = default;
        private float _currentPitchAmount = default;
        private float _currentPitchAmountVelocity = default;
        private float _currentYRotation = default;
        private float _targetYRotation = default;
        private float _targetYRotationVelocity = default;
        private float _currentUpForce = default;

        private float _landingDelay = 2f;
        [SerializeField] private float floorYPos = 0.1564f;
        private float slowDownTimer = 0f;

        [Header("WeatherSimulator")] [SerializeField]

        private float windEffectMultiplier = 0.1f;

        public Vector3 Velocity
        {
            get { return _rigidbody.linearVelocity; }
        }

        private DroneState _currentState = DroneState.Off;

        public DroneState CurrentState
        {
            get { return _currentState; }
        }
        
        private Vector3 startPosition = Vector3.zero;
        private Vector3 startRotation = Vector3.zero;
        private Vector3 homePosition = Vector3.zero;
        private Animator _animator = null;

        private float _timeStartEngine = 2;


        float safeHeight = 0.6f;
        float currentHeight = 0;
        float descentSpeed = 0;
        float kSpeed = 3.0f;
        float kHeight = 10.0f;
        float _opposingForceHover = 0f;

        protected virtual void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.mass = _droneMovementData.Mass;
            _animator = GetComponentInChildren<Animator>();
            _animator.enabled = false;
            inputChannel = GetComponent<FlightInputChannel>();
        }

        protected virtual void Start()
        {
            _timeStartEngine = 2;

            InputManager.OnReturnHomePressed += InputManagerOnReturnHomePressed;
#if UNITY_EDITOR
            InputManager.onPressKeyboard_S += InputManagerOnStartEnginePressed;
#endif

            SetStartingRotation();

            homePosition = new Vector3(transform.position.x, transform.position.y + 0.6f, transform.position.z);
            startPosition = new Vector3(transform.position.x, transform.position.y, transform.position.z);
            startRotation = transform.rotation.eulerAngles;
            
            //Auto start Drone
            DOVirtual.DelayedCall(2, () =>
            {
                InputManagerOnStartEnginePressed();
            });

        }

        private void OnDestroy()
        {
            InputManager.OnReturnHomePressed -= InputManagerOnReturnHomePressed;

#if UNITY_EDITOR
            InputManager.onPressKeyboard_S -= InputManagerOnStartEnginePressed;
#endif
        }

        private void InputManagerOnStartEnginePressed()
        {
            if (_currentState != DroneState.Off) return;
            
            SwitchState(DroneState.StartingEngine);

            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = false;
            DOVirtual.DelayedCall(1, () => { SwitchState(DroneState.ReadyTOFlying); });
        }

        private void InputManagerOnReturnHomePressed()
        {
            if (_currentState != DroneState.Flying) return;

            SwitchState(DroneState.ReturnHome);
            Reset();
            transform.DORotate(startRotation, 4).OnComplete(() =>
            {
                _currentYRotation = default;
                _targetYRotation = default;
            });

            float height = 10;

            transform.DOMoveY(height, (height - transform.position.y) / 9 * 3).SetEase(Ease.Linear).OnComplete(() =>
            {
                transform
                    .DOMove(new Vector3(0, height, 0),
                        Vector3.Distance(transform.position, new Vector3(0, height, 0)) / 9 * 3)
                    .SetEase(Ease.Linear).OnComplete(() =>
                    {
                        transform.DOMove(homePosition, (height) / 9 * 3).OnComplete(() =>
                        {
                            ActiveModeAutoLanding();
                        });
                    });
            });
        }

        private void Reset()
        {
            _smoothDampToStopVelocity = default;
            _currentRollAmount = default;
            _currentRollAmountVelocity = default;
            _currentPitchAmount = default;
            _currentPitchAmountVelocity = default;
            _targetYRotationVelocity = default;
            _currentUpForce = default;

            ClampingSpeedValues();

            ThrottleForce(ReadThrottle());
            RollForce(ReadRoll());
            YawForce(ReadYaw());
            PitchForce(ReadPitch());

            ApplyForces();

            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
        }

        protected virtual void FixedUpdate()
        {
            if (_currentState == DroneState.Off)
            {
                return;
            }

            //CheckHorverLanding();

            if (_currentState == DroneState.PrepareAutoLanding)
            {
                if (ReadThrottle() < 0)
                {
                    _landingDelay -= Time.deltaTime;
                    if (_landingDelay <= 0)
                    {
                        _landingDelay = 2f;
                        ActiveModeAutoLanding();
                    }
                }

                ClampingSpeedValues();

                ThrottleForce(0);
                RollForce(0);
                YawForce(0);
                PitchForce(0);

                ApplyForces();
            }


            if (_currentState == DroneState.ReadyTOFlying || _currentState == DroneState.PrepareAutoLanding)
            {
                if (ReadThrottle() > 0)
                {
                    SwitchState(DroneState.Flying);
                    _animator.enabled = true;
                    _rigidbody.useGravity = true;
                    _rigidbody.isKinematic = false;
                }
            }


            if (_currentState != DroneState.Flying) return;

            ClampingSpeedValues();

            float absThrottle = Mathf.Abs(ReadThrottle());
            float absYaw = Mathf.Abs(ReadYaw());
            bool bothInputsActive = absThrottle > 0.01f && absYaw > 0.01f;

            if (bothInputsActive)
            {
                // Determine which input has larger absolute value
                if (absThrottle > absYaw)
                {
                    // Prioritize throttle - zero out roll
                    ThrottleForce(ReadThrottle());
                    YawForce(0);
                }
                else if (absYaw > absThrottle)
                {
                    // Prioritize roll - zero out throttle
                    ThrottleForce(0);
                    YawForce(ReadYaw());
                }
                else
                {
                    ThrottleForce(ReadThrottle());
                    YawForce(ReadYaw());
                }
            }
            else
            {
                ThrottleForce(ReadThrottle());
                YawForce(ReadYaw());
            }

            RollForce(ReadRoll());
            PitchForce(ReadPitch());

            ApplyForces();
        }

        /// <summary>
        /// Fixes the starting rotation, sets the wanted and current rotation in the
        /// code so drone doesnt start with rotation of (0,0,0).
        /// </summary>
        private void SetStartingRotation()
        {
            _targetYRotation = transform.eulerAngles.y;
            _currentYRotation = transform.eulerAngles.y;
        }

        /// <summary>
        /// Applying upForce for hovering and keeping the drone in the air.
        /// Handles rotation and applies it here.
        /// Handles tilt values and applies it, gues where? here! :)
        /// </summary>
        public void ApplyForces()
        {
            _rigidbody.AddRelativeForce(Vector3.up * _currentUpForce);
                            _rigidbody.rotation = Quaternion.Euler(new Vector3(0, _currentYRotation, 0));
                            _rigidbody.angularVelocity = new Vector3(0, 0, 0);
                            _droneObject.localRotation = Quaternion.Euler(new Vector3(_currentPitchAmount, 0, -_currentRollAmount));
        }

        /// <summary>
        /// Clamping speed values determined on what input is pressed
        /// </summary>
        public void ClampingSpeedValues()
        {
            _rigidbody.linearVelocity = Vector3.ClampMagnitude(_rigidbody.linearVelocity,
                Mathf.Lerp(_rigidbody.linearVelocity.magnitude, _droneMovementData.MaximumPitchSpeed,
                    Time.deltaTime * 5f));

            if (InputManager.Instance.IsInputIdle())
            {
                if (GameManager.Instance.FlightMode == EFlightMode.GPSPositioning)
                {
                    if (_currentUpForce > 14 || _currentUpForce < 13)
                    {
                        //vertical
                        slowDownTimer = 0.1f;
                    }
                    else
                    {
                        //horizontal
                        slowDownTimer = _droneMovementData.SlowDownTimeGPS;
                    }
                }
                else
                {
                    slowDownTimer = _droneMovementData.SlowDownTimeATTI;
                }

                _rigidbody.linearVelocity = Vector3.SmoothDamp(_rigidbody.linearVelocity, Vector3.zero,
                    ref _smoothDampToStopVelocity, slowDownTimer);
            }
        }

        /// <summary>
        /// Handling up down movement and applying needed force.
        /// </summary>
        public void ThrottleForce(float throttleInput)
        {
            float forceValue = (throttleInput > 0) ? _droneMovementData.UpwardMovementForce :
                (throttleInput < 0) ? _droneMovementData.DownwardMovementForce : 0f;
            _currentUpForce = _rigidbody.mass * 9.81f + throttleInput * forceValue;
        }

        /// <summary>
        /// Handling left right movement and appying forces, also handling the titls
        /// </summary>
        public void RollForce(float rollInput)
        {
            _rigidbody.AddRelativeForce(Vector3.right * rollInput * _droneMovementData.SidewardMovementForce);
            _currentRollAmount = Mathf.SmoothDamp(_currentRollAmount, _droneMovementData.MaximumRollAmount * rollInput,
                ref _currentRollAmountVelocity, _droneMovementData.PitchRollTiltSpeed);
        }

        /// <summary>
        /// Handling rotations
        /// </summary>
        public void YawForce(float yawInput)
        {
            _targetYRotation += yawInput * _droneMovementData.MaximumYawSpeed;
            _currentYRotation =
                Mathf.SmoothDamp(_currentYRotation, _targetYRotation, ref _targetYRotationVelocity, 0.25f);
        }

        /// <summary>
        /// Movement forwards and backwars and tilting
        /// </summary>
        public void PitchForce(float pitchInput)
        {
            _rigidbody.AddRelativeForce(Vector3.forward * pitchInput * _droneMovementData.ForwardMovementForce);
            _currentPitchAmount = Mathf.SmoothDamp(_currentPitchAmount,
                _droneMovementData.MaximumPitchAmount * pitchInput, ref _currentPitchAmountVelocity,
                _droneMovementData.PitchRollTiltSpeed);
        }

        private void SwitchState(DroneState newState)
        {
            Debug.Log("OnStateChanged: " + newState);
            _currentState = newState;
            GameManager.Instance.DroneState = newState;
            OnStateChanged?.Invoke(_currentState);
        }

        private void ActiveModeAutoLanding()
        {
            Reset();
            SwitchState(DroneState.AutoLanding);
            Debug.Log("AutoLanding");

            transform.DOMoveY(floorYPos, 3).OnComplete(() =>
            {
                SwitchState(DroneState.Off);
                _animator.enabled = false;
                Debug.Log("Drone OFF");
            });
        }

        private void CheckInputStartEngine()
        {
            //if(GameManager.Instance.IsEditingMode) return;

            // //V down
            // if (InputManager.LeftJoyStickInput.y < 0 && InputManager.LeftJoyStickInput.x < 0)
            // {
            //     if (InputManager.RightJoyStickInput.y < 0 && InputManager.RightJoyStickInput.x > 0)
            //     {
            //         _timeStartEngine -= Time.fixedDeltaTime;
            //         if (_timeStartEngine <= 0)
            //         {
            //             InputManagerOnStartEnginePressed();
            //             _timeStartEngine = 2;
            //         }
            //     }
            // }
            //
            // // V UP
            // if (InputManager.LeftJoyStickInput.y < 0 && InputManager.LeftJoyStickInput.x > 0)
            // {
            //     if (InputManager.RightJoyStickInput.y < 0 && InputManager.RightJoyStickInput.x < 0)
            //     {
            //         _timeStartEngine -= Time.fixedDeltaTime;
            //         if (_timeStartEngine <= 0)
            //         {
            //             InputManagerOnStartEnginePressed();
            //             _timeStartEngine = 2;
            //         }
            //     }
            // }
        }

        private void CheckHorverLanding()
        {
            // if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f))
            // {
            //     if (hit.collider.gameObject.layer != LayerMask.NameToLayer("Ground"))
            //     {
            //         return;
            //     }
            //
            //     if (_currentState == DroneState.Flying && hit.distance < safeHeight && InputManager.ThrottleInput <= 0)
            //     {
            //         SwitchState(DroneState.HorverLanding);
            //     }
            //     else if (_currentState == DroneState.HorverLanding && hit.distance >= safeHeight)
            //     {
            //         SwitchState(DroneState.PrepareAutoLanding);
            //     }
            //
            //     if (_currentState == DroneState.HorverLanding)
            //     {
            //         currentHeight = hit.distance;
            //         descentSpeed = _rigidbody.linearVelocity.y;
            //         _opposingForceHover = -(descentSpeed) * kSpeed + (safeHeight - currentHeight) * kHeight;
            //         ThrottleForce(_opposingForceHover);
            //         ApplyForces();
            //     }
            // }
        }

        public float GetHeightVelocity()
        {
            return _rigidbody.linearVelocity.y;
        }

        public float GetHorizontalVelocity()
        {
            Vector3 horizontalVelocity = new Vector3(_rigidbody.linearVelocity.x, 0, _rigidbody.linearVelocity.z);
            return horizontalVelocity.magnitude;
        }
    }
}