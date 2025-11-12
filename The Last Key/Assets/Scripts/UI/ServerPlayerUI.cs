using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;

public class ServerPlayerUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInputField;
    public TextMeshProUGUI ipAddressText;
    public Button playButton;
    public TextMeshProUGUI statusText;

    private void Start()
    {
        // Display available IPs
        DisplayLocalIP();

        // Setup play button
        if (playButton != null)
            playButton.onClick.AddListener(OnPlayButtonClicked);

        // Set default name
        if (nameInputField != null)
            nameInputField.text = "Host";

        UpdateStatus("Enter your name and click PLAY to start hosting");
    }

    private void DisplayLocalIP()
    {
        string localIP = GetLocalIPAddress();

        if (ipAddressText != null)
        {
            ipAddressText.text = $"Your Server IP: {localIP}\n(Share this IP with other players)";
        }

        Debug.Log($"[ServerPlayerUI] Local IP: {localIP}");
    }

    private string GetLocalIPAddress()
    {
        try
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ServerPlayerUI] Error getting IP: {e.Message}");
        }

        return "127.0.0.1";
    }

    private void OnPlayButtonClicked()
    {
        string playerName = nameInputField != null ? nameInputField.text.Trim() : "Host";

        if (string.IsNullOrEmpty(playerName))
        {
            UpdateStatus("Please enter a valid name!");
            return;
        }

        UpdateStatus("Starting server...");

        // Initialize NetworkManager as Host
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartAsHost(playerName);

            // Wait a moment for server to initialize, then go to waiting room
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
        Debug.Log($"[ServerPlayerUI] {message}");
    }
}