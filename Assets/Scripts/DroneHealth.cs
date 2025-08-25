using System.Collections;
using DroneController;
using UnityEngine;

public class DroneHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHP = 2f;
    [SerializeField] private GameObject explosionPrefab;

    [Header("Freeze Control")]
    [Tooltip("Kéo thả script điều khiển (ví dụ DroneMovement hoặc adapter) để tắt khi đóng băng")]
    private DroneMovement droneMovement;
    private Rigidbody rb;

    private float _hp;
    private bool _isFrozen;
    private Coroutine _freezeCo;

    void Awake()
    {
        _hp = maxHP;
        rb = GetComponent<Rigidbody>();
        droneMovement = GetComponent<DroneMovement>();
    }

    public void ApplyDamage(float dmg)
    {
        if (_hp <= 0f) return;
        _hp -= Mathf.Max(0f, dmg);
        if (_hp <= 0f) Explode();
    }

    public void FreezeAndDamage(float seconds, float dmg)
    {
        ApplyDamage(dmg);
        if (_hp <= 0f) return;

        // Nếu đang đóng băng, gia hạn thay vì tạo coroutine mới
        if (_freezeCo != null)
        {
            StopCoroutine(_freezeCo);
            _freezeCo = StartCoroutine(CoFreeze(seconds)); // gia hạn thời gian
        }
        else
        {
            _freezeCo = StartCoroutine(CoFreeze(seconds));
        }
    }

    private IEnumerator CoFreeze(float seconds)
    {
        _isFrozen = true;
        bool restored = false;

        if (droneMovement && droneMovement.enabled)
        {
            droneMovement.enabled = false;
            restored = true;
        }

        Vector3 oldVel = Vector3.zero;
        Vector3 oldAng = Vector3.zero;
        if (rb)
        {
            oldVel = rb.linearVelocity;
            oldAng = rb.angularVelocity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            // rb.isKinematic = rb.isKinematic; // giữ nguyên chế độ
        }

        float t = Mathf.Max(0f, seconds);
        while (t > 0f && _hp > 0f)
        {
            t -= Time.deltaTime;
            yield return null;
        }

        // Hết đóng băng (nếu chưa chết)
        if (_hp > 0f)
        {
            if (restored && droneMovement)
                droneMovement.enabled = true;

            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        _isFrozen = false;
        _freezeCo = null;
    }

    private void Explode()
    {
        if (explosionPrefab)
        {
            var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 3f);
        }
        Destroy(gameObject);
    }
}
