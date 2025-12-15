using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientJoinUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_InputField usernameInputField;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMPro.TextMeshProUGUI statusText;

    private bool isConnecting = false;

    void Start()
    {
        if (joinButton != null)
        {
            joinButton.onClick.AddListener(OnJoinClicked);
        }
    }

    void Update()
    {
        if (isConnecting)
        {
            NetworkManager networkManager = NetworkManager.Instance;
            if (networkManager != null)
            {
                // Check if connection failed
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
                // Check if connected successfully (networking exists and not failed)
                else if (networkManager.GetNetworking() != null && 
                         !networkManager.IsConnectionFailed())
                {
                    // Connection is active
                    if (statusText != null && statusText.text == "Connecting...")
                    {
                        // Keep showing "Connecting..." until we get to WaitingRoom
                        // The scene change will happen automatically from Networking.cs
                    }
                }
            }
        }
    }

    void OnJoinClicked()
    {
        NetworkManager networkManager = NetworkManager.Instance;
        
        if (networkManager == null)
        {
            Debug.LogError("[ClientJoinUI] NetworkManager not found!");
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
        string targetIP = ipInputField != null ? ipInputField.text : "127.0.0.1";
        string username = usernameInputField != null ? usernameInputField.text : "Player";

        if (string.IsNullOrEmpty(targetIP))
        {
            targetIP = "127.0.0.1";
        }

        if (string.IsNullOrEmpty(username))
        {
            username = "Player";
        }

        // Check if we're retrying a failed connection
        if (networkManager.GetNetworking() != null && networkManager.IsConnectionFailed())
        {
            Debug.Log("[ClientJoinUI] Retrying connection...");
            isConnecting = true;
            bool success = networkManager.RetryClientConnection();
            
            if (!success)
            {
                isConnecting = false;
                if (statusText != null)
                {
                    statusText.text = "Retry failed immediately. Check configuration.";
                }
                if (joinButton != null)
                {
                    joinButton.interactable = true;
                }
            }
        }
        else if (networkManager.currentRole == NetworkManager.NetworkRole.None)
        {
            // First connection attempt
            Debug.Log($"[ClientJoinUI] Starting client connection to {targetIP} as {username}");
            isConnecting = true;
            networkManager.StartAsClient(username, targetIP);
        }
        else
        {
            // Already connected or connecting
            Debug.LogWarning($"[ClientJoinUI] Already in state: {networkManager.currentRole}");
            isConnecting = false;
            
            if (statusText != null)
            {
                statusText.text = "Already connected or connecting...";
            }
            
            if (joinButton != null)
            {
                joinButton.interactable = true;
            }
        }
    }

    void OnDestroy()
    {
        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(OnJoinClicked);
        }
    }
}