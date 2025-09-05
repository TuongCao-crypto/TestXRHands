using UnityEngine;

public enum ETeam
{
    Attacker = 0,
    Defender = 1
}

public enum EOnlineMode
{
    Offline = 0,
    Online = 1
}

public enum EGameMode
{
    GameAR = 0,
    GameVR = 1
}

public class GlobalData : SingletonMonoAwake<GlobalData>
{
   [SerializeField] private ETeam team = ETeam.Attacker;
   public ETeam Team { get { return team; } set { team = value; } }
   
   [SerializeField] private EOnlineMode onlineMode = EOnlineMode.Online;
   public EOnlineMode OnlineMode { get { return onlineMode; } set { onlineMode = value; } }
   
   [SerializeField] private EGameMode gameMode = EGameMode.GameAR;
   public EGameMode GameMode { get { return gameMode; } set { gameMode = value; } }
}
