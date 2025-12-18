using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ServerPlayerUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField nameInputField;
    public TextMeshProUGUI ipAddressText;
    public Button playButton;
    public Button backButton; 
    public TextMeshProUGUI statusText;

    private void Start()
    {
        DisplayLocalIP();

        if (playButton != null)
            playButton.onClick.AddListener(OnPlayButtonClicked);

        
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButtonClicked);

        UpdateStatus("Enter your name and click PLAY to start hosting");
    }

    private void DisplayLocalIP()
    {
        string localIP = GetLocalIPAddress();

        if (ipAddressText != null)
        {
            ipAddressText.text = $"Your Server IP: {localIP}\n(Share this IP with other players)";
        }
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
        catch (System.Exception)
        {
            // Error getting IP - suppressed runtime log
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

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartAsHost(playerName);
        }
        else
        {
            UpdateStatus("Error: NetworkManager not found!");
        }
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

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}