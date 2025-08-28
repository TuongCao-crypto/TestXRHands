using System;
using DroneController;
using UnityEngine;

public class AutoFlightInputController : MonoBehaviour
{
    public enum EFlightStage
    {
        Off = 0,
        MoveToData,
        BackHome,
        Success
    }

    [Header("Main Drone")]
    public bool isMainDrone = false;
    
    private FlightInputChannel _chan;

    [Header("Targets")] public Transform target; // world-space target (Data vault, etc.)

    [Header("Heights & Arrival")] public float hoverHeight = 1.5f; // desired altitude
    public float hoverTolerance = 0.1f;
    public float arriveRadiusTarget = 0.25f; // planar radius to consider "at target"
    public float arriveRadiusHome = 0.35f; // planar radius to consider "back home"

    [Header("Inputs (Limits)")] public float maxPitch = 1f;
    public float maxRoll = 1f;
    public float maxYaw = 1f;

    [Header("Sensitivities")] public float pitchSensitivity = 2f;
    public float rollSensitivity = 2f;
    public float yawSensitivity = 1f; // currently unused (kept for future tuning)

    [Header("Altitude Hold")] public float throttleBase = 9.8f; // base upward force
    public float throttleP = 0.5f; // proportional gain to hold height
    public float throttleClampMax = 20f;

    [Header("Hover Pause")] public float hoverPauseSeconds = 2f;

    private DroneMovement droneMotor;
    private Vector3 originalPosition;
    private bool hoveringComplete = false;
    private float hoverTime;

    private EFlightStage _flightStage = EFlightStage.Off;

    private void SetInputs(float p, float r, float y, float t)
    {
        if (_chan != null)
        {
            _chan.Pitch = p;
            _chan.Roll = r;
            _chan.Yaw = y;
            _chan.Throttle = t;
        }
    }

    private void SetInputs(float p, float r, float y)
    {
        if (_chan != null)
        {
            _chan.Pitch = p;
            _chan.Roll = r;
            _chan.Yaw = y;
        }
    }

    private void SetInputs(float t)
    {
        if (_chan != null)
        {
            _chan.Throttle = t;
        }
    }

    void Start()
    {
        droneMotor = GetComponent<DroneMovement>();
        _chan = GetComponent<FlightInputChannel>();
        originalPosition = transform.position;
        hoverTime = hoverPauseSeconds;

        if (isMainDrone && GlobalData.Instance.Team == ETeam.Defender)
        {
            isMainDrone = false;
        }
    }

    public void SetTarget(Transform target)
    {
        this.target = target;
    }

    void FixedUpdate()
    {
        if (droneMotor == null || droneMotor.CurrentState == DroneState.Off) return;
        
        //logic for Main Drone (user control)
        if (isMainDrone)
        {
            if (_flightStage == EFlightStage.Off)
            {
                _flightStage = EFlightStage.MoveToData;
            }
            else if (_flightStage == EFlightStage.BackHome)
            {
                //reach Home
                if (transform.position.x < 0)
                {
                    //reach Home
                    ScoreManager.Instance.RegisterDataCaptured(transform);
                    _flightStage = EFlightStage.MoveToData;
                }
            }
            return;
        }

        //logic for Auto Drone
        // Stop all angular inputs if Success
        if (_flightStage == EFlightStage.Success || GameManager.Instance.GameStage == EGameStage.Ended)
        {
            SetInputs(0, 0, 0, 0);
            return;
        }

        if (this.target == null && _flightStage == EFlightStage.MoveToData)
        {
            GameManager.Instance.SetDroneNewTarget(this);
            return;
        }

        // --- Altitude control (always on) ---
        float currentHeight = transform.position.y;
        float heightError = hoverHeight - currentHeight;
        float holdThrottle = throttleBase + heightError * throttleP;
        SetInputs(Mathf.Clamp(holdThrottle, 0f, throttleClampMax));

        // --- Step 1: climb to hoverHeight ---
        if (!hoveringComplete)
        {
            // Freeze lateral while acquiring hover altitude
            SetInputs(0, 0, 0);

            if (Mathf.Abs(heightError) <= hoverTolerance)
            {
                hoveringComplete = true;
                _flightStage = EFlightStage.MoveToData;
                Debug.Log("AutoFlight: reached hover band, preparing to move");
            }

            return;
        }

        // --- Optional hover pause before moving ---
        if (hoverTime > 0f)
        {
            hoverTime -= Time.fixedDeltaTime;
            // Keep altitude via holdThrottle; no lateral motion
            SetInputs(0, 0, 0);
            return;
        }

        // --- Step 2: steer towards target/home (planar) ---
        Vector3 dest =
            (_flightStage == EFlightStage.MoveToData)
                ? (target != null ? target.position : transform.position)
                : originalPosition;

        Vector3 toDest = dest - transform.position;
        Vector3 planar = new Vector3(toDest.x, 0f, toDest.z);
        float arriveRadius = (_flightStage == EFlightStage.MoveToData) ? arriveRadiusTarget : arriveRadiusHome;

        if (planar.sqrMagnitude <= arriveRadius * arriveRadius)
        {
            // Reached planar destination for current leg
            if (_flightStage == EFlightStage.BackHome)
            {
                SetInputs(0, 0, 0);
                target = null;
                Debug.Log($"{gameObject.name}: Success (home reached)");
                ScoreManager.Instance.RegisterDataCaptured(transform);
                GameManager.Instance.SetDroneNewTarget(this);
                _flightStage = EFlightStage.MoveToData;
            }
            else
            {
                // At/near Data area; usually OnTriggerEnter will fire to switch stage
                // Keep gentle stop here to avoid overshoot
                SetInputs(0, 0, 0);
                // Note: do NOT change stage here; wait for trigger "Data"
            }

            return;
        }

        // Compute steering using current vector each frame
        Vector3 localDirPlanar = transform.InverseTransformDirection(planar.normalized);

        float pitch = Mathf.Clamp(localDirPlanar.z * pitchSensitivity, -maxPitch, maxPitch);
        float roll = Mathf.Clamp(localDirPlanar.x * rollSensitivity, -maxRoll, maxRoll);

        float angleToDest = Vector3.SignedAngle(transform.forward, planar.normalized, Vector3.up);
        float yaw = Mathf.Clamp(angleToDest / 45f, -1f, 1f) * maxYaw;

        // If turning significantly, temporarily trim pitch/roll to reduce skids
        if (Mathf.Abs(yaw) > 0.01f)
        {
            float trim = 10f * Time.fixedDeltaTime;
            if (pitch > 0f) pitch = Mathf.Max(0f, pitch - trim);
            if (pitch < 0f) pitch = Mathf.Min(0f, pitch + trim);
            if (roll > 0f) roll = Mathf.Max(0f, roll - trim);
            if (roll < 0f) roll = Mathf.Min(0f, roll + trim);
        }
      
        SetInputs(pitch, roll, yaw, 0);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Data") && _flightStage == EFlightStage.MoveToData)
        {
            Debug.Log("Collected Data");
            other.GetComponent<DataPickup>().Collect();
            _flightStage = EFlightStage.BackHome;
        }
    }
}