// FlightInputChannel.cs
using UnityEngine;

public class FlightInputChannel : MonoBehaviour
{
    [Range(0f, 2f)] public float Pitch;
    [Range(0f, 2f)] public float Roll;
    [Range(0f, 2f)] public float Yaw;
    public float Throttle;
}