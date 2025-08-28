using UnityEngine;

public enum ETeam
{
    Attacker = 0,
    Defender = 1
}
public class GlobalData : SingletonMonoAwake<GlobalData>
{
   [SerializeField] private ETeam team = ETeam.Attacker;
   public ETeam Team { get { return team; } set { team = value; } }
}
