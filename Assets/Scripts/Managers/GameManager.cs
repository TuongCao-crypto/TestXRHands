using System;
using System.Collections.Generic;
using DG.Tweening;
using DroneController;
using UnityEngine;

public enum EFlightMode
{
    GPSPositioning = 0,
    ATTI
}

public enum EGameStage
{
    None = 0,
    Live,
    Ended
}

public class GameManager : SingletonMonoAwake<GameManager>
{
    private EFlightMode _flightMode = EFlightMode.GPSPositioning;

    public EFlightMode FlightMode
    {
        set
        {
            _flightMode = value;
            PlayerPrefs.SetInt("_flightMode", (int)_flightMode);
        }
        get { return _flightMode; }
    }

    private DroneState _droneState = DroneState.Off;

    public DroneState DroneState
    {
        get { return _droneState; }
        set { _droneState = value; }
    }

    public EGameStage GameStage = EGameStage.None;


    [SerializeField] AttackerManager _attackerManager;
    [SerializeField] DefenderDataManager _defenderDataManager;
    [SerializeField] private Transform cameraRig;

    private void Start()
    {
        StartGame();
    }

    public void StartGame()
    {
        switch (GlobalData.Instance.Team)
        {
            case ETeam.Attacker:
                cameraRig.position = new Vector3(-4f, 0, 0);
                cameraRig.rotation = Quaternion.Euler(0, 90, 0);
                break;

            case ETeam.Defender:
                cameraRig.position = new Vector3(9.35f, 0, 0);
                cameraRig.rotation = Quaternion.Euler(0, -90, 0);
                break;
        }

        _attackerManager.SetDroneInitTargets(_defenderDataManager.ActivePickups);
        GameStage = EGameStage.Live;
        ScoreManager.Instance.StartMatch();
    }

    public void EndGame()
    {
        GameStage = EGameStage.Ended;
        // DOVirtual.DelayedCall(5, () =>
        // {
        //     //auto re-start game
        //     StartGame();
        // });
    }

    public void SetDroneNewTarget(AutoFlightInputController drone)
    {
        _attackerManager.SetDroneTarget(drone, _defenderDataManager.ActivePickups);
    }
}