using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuPanel : MonoBehaviour
{
    //Online
    [SerializeField] private ToggleGroup mutiplayerGroup;
    [SerializeField] private Toggle[] mutiplayerToggles;

    //mode
    [SerializeField] private ToggleGroup modeGroup;
    [SerializeField] private Toggle[] modeToggles;

    public void StartGame(int team)
    {
        //team
        GlobalData.Instance.Team = (ETeam)team;

        //online mode
        ToggleGroupUtils.EnsureOneOn(mutiplayerGroup);
        int idxMuti = ToggleGroupUtils.GetSelectedIndex(mutiplayerGroup, mutiplayerToggles);
        GlobalData.Instance.OnlineMode = (EOnlineMode)idxMuti;

        //game mode
        ToggleGroupUtils.EnsureOneOn(modeGroup);
        int idx = ToggleGroupUtils.GetSelectedIndex(modeGroup, modeToggles);
        GlobalData.Instance.GameMode = (EGameMode)idx;


        //Load scene
        switch (GlobalData.Instance.OnlineMode)
        {
            case EOnlineMode.Offline:
                SceneManager.LoadScene(GlobalData.Instance.GameMode.ToString());
                break;
            case EOnlineMode.Online:
                SceneManager.LoadScene("Lobby");
                break;
        }
    }
}