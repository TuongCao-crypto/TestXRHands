// FlightInputChannel.cs
using UnityEngine;

public class FlightInputChannel : MonoBehaviour
{
    [Range(0f, 2f)] public float Pitch;
    [Range(0f, 2f)] public float Roll;
    [Range(0f, 2f)] public float Yaw;
    public float Throttle;
    
    public bool IsInputIdle()
    {
        return Mathf.Approximately(Pitch, 0f) && Mathf.Approximately(Roll, 0f) &&
               Mathf.Approximately(Yaw, 0f);
    }
}