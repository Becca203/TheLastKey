using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientJoinUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInputField;
    public TMP_InputField ipInputField;
    public Button connectButton;
    public TextMeshProUGUI statusText;

    private bool isConnecting = false;
    private float connectionTimer = 0f;
    private float connectionTimeout = 10f;

    private void Start()
    {
        if (connectButton != null)
            connectButton.onClick.AddListener(OnConnectButtonClicked);

        UpdateStatus("Enter server IP and your name to join");
    }

    private void Update()
    {
        // Show connection progress
        if (isConnecting)
        {
            connectionTimer += Time.deltaTime;
            
            if (connectionTimer > connectionTimeout)
            {
                UpdateStatus("Connection timeout! Please check the server IP and try again.");
                isConnecting = false;
                
                if (connectButton != null)
                    connectButton.interactable = true;
            }
            else
            {
                int dots = Mathf.FloorToInt(connectionTimer * 2) % 4;
                string dotsString = new string('.', dots);
                UpdateStatus($"Connecting{dotsString}");
            }
        }
    }

    private void OnConnectButtonClicked()
    {
        string playerName = nameInputField != null ? nameInputField.text.Trim() : "";
        string serverIP = ipInputField != null ? ipInputField.text.Trim() : "";

        // Validation
        if (string.IsNullOrEmpty(playerName))
        {
            UpdateStatus("Please enter a valid name!");
            return;
        }

        if (string.IsNullOrEmpty(serverIP))
        {
            UpdateStatus("Please enter a valid server IP!");
            return;
        }

        // Disable button while connecting
        if (connectButton != null)
            connectButton.interactable = false;

        UpdateStatus($"Connecting to {serverIP}...");
        isConnecting = true;
        connectionTimer = 0f;

        // Initialize NetworkManager as Client
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartAsClient(playerName, serverIP);
            Debug.Log($"[ClientJoinUI] Connecting to {serverIP} with username '{playerName}'");
        }
        else
        {
            UpdateStatus("Error: NetworkManager not found!");
            isConnecting = false;
            
            if (connectButton != null)
                connectButton.interactable = true;
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[ClientJoinUI] {message}");
    }
}