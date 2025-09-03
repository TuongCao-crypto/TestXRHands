using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuPanel : MonoBehaviour
{
    [SerializeField] private ToggleGroup modeGroup;
    [SerializeField] private Toggle[] modeToggles;

    public void StartGame(int team)
    {
        GlobalData.Instance.Team = (ETeam)team;

        ToggleGroupUtils.EnsureOneOn(modeGroup);

        // bool any = ToggleGroupUtils.AnyOn(modeGroup);                 // check any ON
        // Toggle current = ToggleGroupUtils.GetSelected(modeGroup);     // get current ON
        int idx = ToggleGroupUtils.GetSelectedIndex(modeGroup, modeToggles);
        switch (idx)
        {
            case 0:
                SceneManager.LoadScene("GameAR");
                break;
            case 1:
                SceneManager.LoadScene("GameMR");
                break;
            default:
                SceneManager.LoadScene("GameAR");
                break;
        }
    }
}