using System.Collections.Generic;
using UnityEngine;

public class AttackerManager : MonoBehaviour
{
    private Dictionary<int, DroneHealth> dronesHealth;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //dronesHealth = new List<DroneHealth>(GetComponentsInChildren<DroneHealth>(true));
        InitializeDroneHealth();
    }

    public void InitializeDroneHealth()
    {
        // foreach (var dr in dronesHealth)
        // {
        //     dr.Init();
        // }
    }

    public void OnDroneBroken()
    {
    }
}