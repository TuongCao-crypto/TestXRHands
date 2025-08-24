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

    public Transform target; // Target position to fly towards
    private Vector3 targetPosition;
    private Vector3 originalPosition;

    public float hoverHeight = 1.5f; // Height to reach, then hold
    public float maxPitch = 1f;
    public float maxRoll = 1f;
    public float maxYaw = 1f;
    public float throttle = 9.8f; // Base upward force to climb to hover height

    public float pitchSensitivity = 2f;
    public float rollSensitivity = 2f;
    public float yawSensitivity = 1f;

    public float hoverTolerance = 0.1f; // Height tolerance to consider "at hover height"

    private DroneMovement droneMotor;
    private bool hoveringComplete = false;
    private float hoverTime = 2f;

    private EFlightStage _flightStage = EFlightStage.Off;

    void Start()
    {
        droneMotor = GetComponent<DroneMovement>();
        targetPosition = target.position - transform.position;
        originalPosition = transform.position;
    }

    void FixedUpdate()
    {
        if (droneMotor == null || InputManager.Instance == null) return;
        // Drone must be powered on (any state except Off)
        if (droneMotor.CurrentState == DroneState.Off) return;

        if (_flightStage == EFlightStage.Success)
        {
            InputManager.Instance.PitchInput = 0f;
            InputManager.Instance.RollInput = 0f;
            InputManager.Instance.YawInput = 0f;
            return;
        }

        // --- Step 1: Climb to hoverHeight ---
        float currentHeight = transform.position.y;
        float heightError = hoverHeight - currentHeight;

        if (!hoveringComplete)
        {
            // Drive up/down until we reach hoverHeight within tolerance
            float climbThrottle = throttle + heightError * 0.5f; // Gain for faster convergence
            InputManager.Instance.ThrottleInput = Mathf.Clamp(climbThrottle, 0f, 20f);

            // No lateral motion while climbing
            InputManager.Instance.PitchInput = 0f;
            InputManager.Instance.RollInput = 0f;
            InputManager.Instance.YawInput = 0f;

            if (Mathf.Abs(heightError) <= hoverTolerance)
            {
                hoveringComplete = true;
                Debug.Log("AutoFlightInputController: Hover over climb threshold exceeded");
                _flightStage = EFlightStage.MoveToData;
            }
            return;
        }

        // Hover 2s prepare for move to target
        if (hoverTime > 0)
        {
            hoverTime -= Time.fixedDeltaTime;
            InputManager.Instance.ThrottleInput = 0;
            return;
        }

        // --- Step 2: Hold altitude (zero throttle) & move toward target ---
        // Per requirement: once at hoverHeight, keep throttle = 0 and only steer to target.

        Vector3 planarToTarget = new Vector3(targetPosition.x, 0f, targetPosition.z);

        if (planarToTarget.sqrMagnitude < 0.001f)
        {
            InputManager.Instance.PitchInput = 0f;
            InputManager.Instance.RollInput = 0f;
            InputManager.Instance.YawInput = 0f;
            Debug.Log(gameObject.name + "Hit target");
            return;
        }

        //Move to target
        Vector3 localDirPlanar = transform.InverseTransformDirection(planarToTarget.normalized);
        float pitch = Mathf.Clamp(localDirPlanar.z * pitchSensitivity, -maxPitch, maxPitch);
        float roll = Mathf.Clamp(localDirPlanar.x * rollSensitivity, -maxRoll, maxRoll);
        float angleToTarget = Vector3.SignedAngle(
            transform.forward,
            planarToTarget.normalized,
            Vector3.up
        );
        float yaw = Mathf.Clamp(angleToTarget / 45f, -1f, 1f) * maxYaw;

        if (yaw > 0.01f)
        {
            if (pitch > 0) pitch -= Time.fixedDeltaTime * 10f;
            if (pitch < 0) pitch = 0;

            if (roll > 0) roll -= Time.fixedDeltaTime * 10f;
            if (roll < 0) roll = 0;

        }

        if (_flightStage == EFlightStage.BackHome && AreVectorsEqual(originalPosition, transform.position))
        {
            InputManager.Instance.PitchInput = 0;
            InputManager.Instance.RollInput = 0;
            InputManager.Instance.YawInput = 0;
            InputManager.Instance.ThrottleInput = 0;
            _flightStage = EFlightStage.Success;
            Debug.Log(gameObject.name + ": Success!");
        }
        else
        {
            InputManager.Instance.PitchInput = pitch;
            InputManager.Instance.RollInput = roll;
            InputManager.Instance.YawInput = yaw;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Data"))
        {
            Debug.Log("Collected Data");
            targetPosition = originalPosition - transform.position;
            other.gameObject.SetActive(false);
            _flightStage = EFlightStage.BackHome;
        }
    }

    private bool AreVectorsEqual(Vector3 v1, Vector3 v2, float tolerance = 1f)
    {
        return Vector3.Distance(new Vector3(v1.x, 0, v1.z), new Vector3(v2.x, 0, v2.z)) <= tolerance;
    }
}