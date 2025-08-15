using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace DroneController
{
    [RequireComponent(typeof(DroneMovement))]
    public class PropellerMovement : MonoBehaviour
    {
        [Header("Local References:")] [SerializeField]
        private Transform[] _propellers = default;

        [SerializeField] private SimpleSpinBlur[] _blurs = default;

        [SerializeField] private Oculus.Interaction.Samples.ConstantRotation[] _propellersFake = default;

        [Header("Settings:")] [SerializeField] private float _rotationSpeed = 3f;
        [SerializeField] private float _velocityMultiplier = .3f;


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
            foreach (var propeller in _propellersFake)
            {
                propeller.gameObject.SetActive(false);
            }

            SetPropsFake(0, 0);
            _rotationSpeed = 0;

            DroneMovement.OnStateChanged += (newState) =>
            {
                if (lastState == DroneState.Off && newState == DroneState.StartingEngine)
                {
                    _rotationSpeed = 0;
                    DOTween.To(() => _rotationSpeed, x => { _rotationSpeed = x; }, 55,
                        2f).OnComplete(() =>
                    {
                        foreach (var propeller in _propellersFake)
                        {
                            propeller.gameObject.SetActive(true);
                        }

                        SetPropsFake(-20, 2);
                    });

                    FadeBlur(1, 128, 30, 50, 2);
                }
                else if (lastState == DroneState.ReadyTOFlying && newState == DroneState.Flying)
                {
                    //DOTween.To(() => _rotationSpeed, x => { _rotationSpeed = x; }, 55, 1f);
                    //SetPropsFake(40f / 255f, 0.5f, 1);
                }
                else if (newState == DroneState.AutoLanding)
                {
                    DOTween.To(() => _rotationSpeed, x => { _rotationSpeed = x; }, 0, 6f);
                    SetPropsFake(0, 3);
                    foreach (var propeller in _propellersFake)
                    {
                        propeller.gameObject.SetActive(false);
                    }

                    FadeBlur(128, 1, 50, 30, 5);

                }

                lastState = newState;
            };
        }

        protected virtual void Update()
        {
            float calculatedRotationSpeed = _rotationSpeed + (DroneMovement.Velocity.magnitude * _velocityMultiplier);

            for (int i = 0; i < _propellers.Length; i++)
            {
                calculatedRotationSpeed *= (i % 2 == 0) ? 1 : -1;
                _propellers[i].Rotate(Vector3.up, calculatedRotationSpeed, Space.Self);
            }
        }

        private void SetPropsFake(float speed, float duration)
        {
            foreach (var propeller in _propellersFake)
            {
                propeller.RotationSpeed = speed;
            }
           
        }

        private void FadeBlur(int shutterStart, int shutterEnd, int sampleStart, int sampleEnd, float duration)
        {
            for (int i = 0; i < _blurs.Length; i++)
            {
                int index = i;
                int shutterSpeed = shutterStart;
                DOTween.To(() => shutterSpeed, x =>
                    {
                        shutterSpeed = x;
                        _blurs[index].shutterSpeed = shutterSpeed;
                    }, shutterEnd,
                    duration);

                int sample = sampleStart;
                DOTween.To(() => sample, x =>
                    {
                        sample = x;
                        _blurs[index].Samples = sample;
                    }, sampleEnd,
                    duration);
            }
        }
    }
}