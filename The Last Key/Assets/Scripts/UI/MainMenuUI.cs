using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Main Menu Buttons")]
    public Button hostButton;
    public Button joinButton;
    public Button exitButton;

    private void Start()
    {
        // Ensure NetworkManager exists
        if (NetworkManager.Instance == null)
        {
            GameObject nmObj = new GameObject("NetworkManager");
            nmObj.AddComponent<NetworkManager>();
        }

        // Setup button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostButtonClicked);

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinButtonClicked);

        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitButtonClicked);
    }

    private void OnHostButtonClicked()
    {
        Debug.Log("[MainMenu] Host button clicked");
        SceneManager.LoadScene("ServerPlayerScene");
    }

    private void OnJoinButtonClicked()
    {
        Debug.Log("[MainMenu] Join button clicked");
        SceneManager.LoadScene("ClientJoinScene");
    }

    private void OnExitButtonClicked()
    {
        Debug.Log("[MainMenu] Exit button clicked");
        Application.Quit();
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}
