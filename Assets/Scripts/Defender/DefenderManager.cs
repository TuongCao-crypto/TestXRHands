using System.Collections.Generic;
using UnityEngine;

public class DefenderManager : MonoBehaviour
{
    [SerializeField] List<GameObject> actives;
    [SerializeField] List<GameObject> deActives;
    void Start()
    {
        foreach (var ctr in actives)
        {
            if(ctr == null) continue;
            ctr.SetActive(GlobalData.Instance.Team == ETeam.Defender);
        }
        
        foreach (var ctr in deActives)
        {
            if(ctr == null) continue;
            ctr.SetActive(GlobalData.Instance.Team != ETeam.Defender);
        }
    }
}
