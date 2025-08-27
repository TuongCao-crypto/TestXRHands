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
    
    private void Start()
    {
        StartGame();
    }
    
    public void StartGame()
    {
        GameStage = EGameStage.Live;
        _attackerManager.SetDroneInitTargets(_defenderDataManager.ActivePickups);
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