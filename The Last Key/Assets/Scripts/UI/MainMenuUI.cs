using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        SceneManager.LoadScene("ServerPlayerScene");
    }

    private void OnJoinButtonClicked()
    {
        SceneManager.LoadScene("ClientJoinScene");
    }

    private void OnExitButtonClicked()
    {
        Application.Quit();
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}
