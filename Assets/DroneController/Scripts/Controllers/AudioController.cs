using DG.Tweening;
using UnityEngine;

namespace DroneController
{
    public class AudioController : MonoBehaviour
    {
        [Header("Local References:")] [SerializeField]
        private AudioSource _audioSource = default;

        [Header("Settings:")] [SerializeField] private float _volume = 0.1f;
        [SerializeField] private float _volumeVelocityMultiplier = 0.07f;
        [Space] [SerializeField] private float _pitch = 1f;
        [SerializeField] private float _pitchVelocityMultiplier = 0.07f;

        private DroneMovement _droneMovement = default;

        private DroneMovement DroneMovement
        {
            get
            {
                if (_droneMovement == null)
                {
                    _droneMovement = GetComponent<DroneMovement>();
                }

                return _droneMovement;
            }
        }

        private DroneState lastState = DroneState.Off;

        private void Start()
        {
            _volume = 0;
            DroneMovement.OnStateChanged += (newState) =>
            {
                if (lastState == DroneState.Off && newState == DroneState.StartingEngine)
                {
                    DOTween.To(() => _volume, x =>
                    {
                        _volume = x;
                    }, 0.4f, 2f);
                }
                else if (lastState == DroneState.ReadyTOFlying && newState == DroneState.Flying)
                {
                    DOTween.To(() => _volume, x =>
                    {
                        _volume = x;
                    }, 0.7f, 2f);
                }
                
                else if (newState == DroneState.AutoLanding)
                {
                    DOTween.To(() => _volume, x =>
                    {
                        _volume = x;
                    }, 0, 4f);
                }

                lastState = newState;
            };
        }

        protected virtual void Update()
        {

            float calculatedVolume = _volume + (DroneMovement.Velocity.magnitude * _volumeVelocityMultiplier);
            float calculatedPitch = _pitch + (DroneMovement.Velocity.magnitude * _pitchVelocityMultiplier);
            _audioSource.volume = calculatedVolume;
            _audioSource.pitch = calculatedPitch;
        }
    }
}