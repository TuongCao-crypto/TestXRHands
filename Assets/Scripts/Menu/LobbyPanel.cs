using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyPanel : MonoBehaviour
{
    
    public void StartGame()
    {
        SceneManager.LoadScene(GlobalData.Instance.GameMode.ToString());
    }
}