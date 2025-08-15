using System;
using System.Collections.Generic;
using DroneController;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum EGameMode
{
    Offline = 0,
    Online
}

public enum EScreenMode
{
    Passthrough_Unrestricted = 1,
    Passthrough_Occlusion,
    Passthrough_CollisionAware,
    Overlay,
    ImmersiveInHouse,
    ImmersiveOutDoor
}

public enum EFlightMode
{
    GPSPositioning = 0,
    ATTI
}

public class GameManager : SingletonMonoAwake<GameManager>
{


    [SerializeField] private EFlightMode _flightMode = EFlightMode.GPSPositioning;

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


    public override void OnAwake()
    {
        base.OnAwake();
    }

   

    public void StartGame()
    {
       
    }

   

    public void LoadNewMode()
    {
        _droneState = DroneState.Off;
     
    }

}