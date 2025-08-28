using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuPanel : MonoBehaviour
{
    public void StartGame(int team)
    {
        GlobalData.Instance.Team = (ETeam)team;
        SceneManager.LoadScene("Game");
    }
}
