using UnityEngine;

public class LoadingRotate : MonoBehaviour
{
    [SerializeField] float rotationSpeed = 5f;
    void Update()
    {
        transform.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
    }
}
