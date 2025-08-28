using System;
using System.Collections;
using DroneController;
using UnityEngine;
using TMPro;

public class DroneHealth : MonoBehaviour
{
    [Header("Main Drone")]
    public bool isMainDrone = false;
    
    [Header("Health")]
    [SerializeField] private float maxHP = 2f;
    [SerializeField] private GameObject explosionPrefab;

    [Header("Shield")]
    [SerializeField] GameObject shieldObj;
    private bool shieldActive = false;
    
    [Header("Hit / Freeze FX")]
    [Tooltip("Optional anchor (e.g., top/center of the drone) for spawning VFX & countdown text.")]
    [SerializeField] private Transform effectAnchor;
    [Tooltip("One-shot effect when the drone is hit by a beam.")]
    [SerializeField] private GameObject hitEffectPrefab;
    [Tooltip("Looping effect while the drone is frozen (enabled during freeze, disabled after).")]
    [SerializeField] private GameObject freezeEffectPrefab;

    [Header("Freeze Countdown (TextMeshPro)")]
    [Tooltip("Text to display remaining freeze seconds. If null and autoCreate is true, a TMP object will be created at runtime.")]
    [SerializeField] private TextMeshPro countdownText;
    [SerializeField] private bool autoCreateCountdownText = true;
    [SerializeField] private Vector3 countdownLocalOffset = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private Color countdownColor = new Color(0.2f, 0.9f, 1f, 1f);
    [SerializeField] private float countdownFontSize = 1.2f;

    [Header("Freeze Control")]
    [Tooltip("Control script to disable while frozen (e.g., DroneMovement or adapter).")]
    private DroneMovement droneMovement;
    private Rigidbody rb;

    private float _hp;
    private bool _isFrozen;
    private Coroutine _freezeCo;
    private GameObject _freezeFxInstance;

    private Transform Anchor => effectAnchor != null ? effectAnchor : transform;

    private AttackerManager _attackerManager;
    public int ID = 0;
    
    void Awake()
    {
        _hp = maxHP;
        rb = GetComponent<Rigidbody>();
        droneMovement = GetComponent<DroneMovement>();

        // Auto-create countdown text if requested and not assigned
        if (countdownText == null && autoCreateCountdownText)
        {
            var go = new GameObject("FreezeCountdown_TMP");
            go.transform.SetParent(Anchor, false);
            go.transform.localPosition = countdownLocalOffset;
            countdownText = go.AddComponent<TextMeshPro>();
            countdownText.alignment = TextAlignmentOptions.Center;
            countdownText.color = countdownColor;
            countdownText.fontSize = countdownFontSize;
            countdownText.enableAutoSizing = false;
            countdownText.text = "";
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
       
    }

    public void Init(int id,AttackerManager parent)
    {
        ID = id;
        _attackerManager = parent;
        if (isMainDrone && GlobalData.Instance.Team == ETeam.Defender)
        {
            isMainDrone = false;
        }
    }

    private void Update()
    {
        if (shieldActive)
        {
            if (transform.position.x > 0)
            {
                if(shieldObj != null) shieldObj.SetActive(false);
                shieldActive = false;
            }
        }
        else
        {
            if (transform.position.x < 0)
            {
                if(shieldObj != null) shieldObj.SetActive(true);
                shieldActive = true;
            }
        }
    }

    /// <summary>
    /// Apply damage to the drone. Triggers hit VFX. Explodes if HP reaches 0.
    /// </summary>
    public void ApplyDamage(float dmg)
    {
        if(shieldActive) return;
        
        if (_hp <= 0f) return;

        // Spawn one-shot hit VFX
        SpawnOneShotEffect(hitEffectPrefab, Anchor.position);

        _hp -= Mathf.Max(0f, dmg);
        if (_hp <= 0f)
        {
            Explode();
        }
        else
        {
            ScoreManager.Instance.RegisterDroneTakeDamge();
        }
    }

    /// <summary>
    /// Apply damage and freeze the drone for the given duration (seconds).
    /// If already frozen, extends the freeze duration.
    /// </summary>
    public void FreezeAndDamage(float seconds, float dmg)
    {
        ApplyDamage(dmg);
        if (_hp <= 0f) return;

        if (_freezeCo != null)
        {
            StopCoroutine(_freezeCo);
            _freezeCo = StartCoroutine(CoFreeze(seconds)); // extend freeze time
        }
        else
        {
            _freezeCo = StartCoroutine(CoFreeze(seconds));
        }
    }

    /// <summary>
    /// Handles freeze state: disables movement, zeros velocity, shows looping freeze VFX and countdown,
    /// then restores control after time passes (if not destroyed).
    /// </summary>
    private IEnumerator CoFreeze(float seconds)
    {
        _isFrozen = true;

        // Disable movement script while frozen
        bool restoredMovement = false;
        if (droneMovement && droneMovement.enabled)
        {
            droneMovement.enabled = false;
            restoredMovement = true;
        }

        // Zero velocities while frozen
        Vector3 oldVel = Vector3.zero;
        Vector3 oldAng = Vector3.zero;
        if (rb)
        {
            // NOTE: Keeping both 'velocity' and 'linearVelocity' in case a custom wrapper is in use.
            oldVel = rb.linearVelocity;
            oldAng = rb.angularVelocity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // If your custom Rigidbody supports linearVelocity, uncomment the lines below:
            // rb.linearVelocity = Vector3.zero;
            // rb.angularVelocity = Vector3.zero;
        }

        // Ensure freeze VFX is active
        EnableFreezeFx(true);

        // Show countdown
        if (countdownText != null)
            countdownText.gameObject.SetActive(true);

        float t = Mathf.Max(0f, seconds);
        while (t > 0f && _hp > 0f)
        {
            if (countdownText != null)
                countdownText.text = $"{t:0.0}s";

            t -= Time.deltaTime;
            yield return null;
        }

        // Hide countdown (if still alive)
        if (_hp > 0f && countdownText != null)
        {
            countdownText.text = "";
            countdownText.gameObject.SetActive(false);
        }

        // Restore after freeze if not destroyed
        if (_hp > 0f)
        {
            if (restoredMovement && droneMovement)
                droneMovement.enabled = true;

            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                // If your custom Rigidbody supports linearVelocity, uncomment:
                // rb.linearVelocity = Vector3.zero;
                // rb.angularVelocity = Vector3.zero;
            }
        }

        // Disable freeze FX
        EnableFreezeFx(false);

        _isFrozen = false;
        _freezeCo = null;
    }

    /// <summary>
    /// Spawns explosion VFX and destroys the drone.
    /// </summary>
    private void Explode()
    {
        _attackerManager.OnDroneBroken(ID);
        ScoreManager.Instance.RegisterDroneDowned();
        // Turn off countdown and freeze FX if any
        if (countdownText != null)
        {
            countdownText.text = "";
            countdownText.gameObject.SetActive(false);
        }
        EnableFreezeFx(false);

        if (explosionPrefab)
        {
            var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 3f);
        }
        Destroy(gameObject);
    }

    /// <summary>
    /// Spawns a one-shot VFX prefab at position, destroys it after its particle duration if any,
    /// otherwise after 2 seconds.
    /// </summary>
    private void SpawnOneShotEffect(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, position, Quaternion.identity);
        float lifetime = 2f;

        var ps = go.GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            lifetime = main.duration + main.startLifetimeMultiplier;
        }

        Destroy(go, lifetime);
    }

    /// <summary>
    /// Enables/disables the looping freeze FX. Instantiates it once and toggles active state.
    /// </summary>
    private void EnableFreezeFx(bool enable)
    {
        if (!freezeEffectPrefab) return;

        if (enable)
        {
            if (_freezeFxInstance == null)
            {
                _freezeFxInstance = Instantiate(freezeEffectPrefab, Anchor);
                _freezeFxInstance.transform.localPosition = Vector3.zero;
                _freezeFxInstance.transform.localRotation = Quaternion.identity;
            }
            _freezeFxInstance.SetActive(true);
        }
        else
        {
            if (_freezeFxInstance != null)
                _freezeFxInstance.SetActive(false);
        }
    }
}
