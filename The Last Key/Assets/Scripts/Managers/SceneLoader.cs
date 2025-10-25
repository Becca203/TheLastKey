using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    // When Return to main menu button pressed
    public void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenu"); 
    }

    // When both player are connected
    public void LoadGameScene() 
    {
        SceneManager.LoadScene("GameScene");
    }

    // When button play pressed
    public void LoadWaitingRoom()
    {
        SceneManager.LoadScene("WaitingRoom");
    }
}
