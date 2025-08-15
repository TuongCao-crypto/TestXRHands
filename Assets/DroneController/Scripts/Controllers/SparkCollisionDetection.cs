using UnityEngine;

namespace DroneController
{
    [RequireComponent(typeof(Rigidbody))]
    public class SparkCollisionDetection : MonoBehaviour
    {
        public delegate void SparkCollisionDetectionEventHandler(ContactPoint contactPoint);
        public static event SparkCollisionDetectionEventHandler CollisionDetected;

        protected virtual void OnCollisionStay(Collision collision)
        {
            if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
                return; // Ignore Ground collisions
            
            CollisionDetected?.Invoke(collision.GetContact(0));
        }
    }
}
