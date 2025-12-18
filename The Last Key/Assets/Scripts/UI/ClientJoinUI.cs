using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientJoinUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_InputField usernameInputField;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button backButton; 
    [SerializeField] private TextMeshProUGUI statusText;

    private bool isConnecting = false;

    void Start()
    {
        if (joinButton != null)
        {
            joinButton.onClick.AddListener(OnJoinClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
    }

    void Update()
    {
        if (isConnecting)
        {
            NetworkManager networkManager = NetworkManager.Instance;
            if (networkManager != null)
            {
                if (networkManager.IsConnectionFailed())
                {
                    isConnecting = false;
                    
                    if (statusText != null)
                    {
                        statusText.text = "Connection failed. Check server and retry.";
                    }
                    
                    if (joinButton != null)
                    {
                        joinButton.interactable = true;
                    }
                }
                else if (networkManager.GetNetworking() != null && !networkManager.IsConnectionFailed())
                {
                    if (statusText != null && statusText.text == "Connecting...") {}
                }
            }
        }
    }

    void OnJoinClicked()
    {
        NetworkManager networkManager = NetworkManager.Instance;
        
        if (networkManager == null)
        {
            if (statusText != null)
            {
                statusText.text = "Error: NetworkManager not found!";
            }
            return;
        }

        // Disable button during connection
        if (joinButton != null)
        {
            joinButton.interactable = false;
        }

        if (statusText != null)
        {
            statusText.text = "Connecting...";
        }

        // Get input values
        string targetIP = ipInputField != null ? ipInputField.text.Trim() : "127.0.0.1";
        string username = usernameInputField != null ? usernameInputField.text.Trim() : "Player";

        if (string.IsNullOrEmpty(targetIP))
        {
            targetIP = "127.0.0.1";
        }

        if (string.IsNullOrEmpty(username))
        {
            username = "Player";
        }

        // Attempting to connect; no runtime log
        networkManager.StartAsClient(username, targetIP);
        isConnecting = true;
    }

    private void OnBackButtonClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReturnToMainMenu();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}