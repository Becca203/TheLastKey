using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ClientJoinUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInputField;
    public TMP_InputField ipInputField;
    public Button connectButton;
    public TextMeshProUGUI statusText;

    private void Start()
    {
        // Setup connect button
        if (connectButton != null)
            connectButton.onClick.AddListener(OnConnectButtonClicked);

        // Set default values
        if (nameInputField != null)
            nameInputField.text = "Player";

        if (ipInputField != null)
            ipInputField.text = "127.0.0.1";

        UpdateStatus("Enter server IP and your name to join");
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

        UpdateStatus($"Connecting to {serverIP}...");

        // Initialize NetworkManager as Client
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartAsClient(playerName, serverIP);

            // Go to waiting room (client will auto-connect)
            Invoke(nameof(GoToWaitingRoom), 0.5f);
        }
        else
        {
            UpdateStatus("Error: NetworkManager not found!");
        }
    }

    private void GoToWaitingRoom()
    {
        SceneManager.LoadScene("WaitingRoom");
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