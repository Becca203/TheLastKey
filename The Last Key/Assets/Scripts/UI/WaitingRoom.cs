using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WaitingRoom : MonoBehaviour
{
    public TextMeshProUGUI connectedPlayersText;
    public TextMeshProUGUI chatText;
    public TMP_InputField chatMessageInput;
    public Button sendButton;
    public TextMeshProUGUI roomInfoText;
    public TextMeshProUGUI ipAddressText;  

    public Button playButton;
    public int minPlayersToStart = 2; 

    // Data structures to store player and chat information
    private List<string> connectedPlayers = new List<string>();
    private List<string> chatMessages = new List<string>();

    private void Start()
    {
        AddChatMessage("System", "Welcome to the waiting room!");
        if (IsServer())
            DisplayLocalIP();

        if (playButton != null)
        {
            playButton.gameObject.SetActive(true);
            playButton.interactable = false;
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }

        // Add the local player to the room immediately
        string myUsername = GetMyUsername();
        if (!string.IsNullOrEmpty(myUsername))
        {
            AddPlayer(myUsername);
            // Local player added; no runtime log
        }
        
        UpdateInfo();
    }

    
    private void DisplayLocalIP()
    {
        string localIP = GetLocalIPAddress();

        if (ipAddressText != null)
        {
            ipAddressText.text = $"Server IP: {localIP}";
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
            // Error while obtaining IP; suppressed runtime log
        }

        return "127.0.0.1";
    }

    public void OnPlayButtonClicked()
    {
        Networking networking = FindAnyObjectByType<Networking>();
        if (networking != null)
        {
            SimpleMessage startMsg = new SimpleMessage("START_GAME", "");
            byte[] data = NetworkSerializer.Serialize(startMsg);
            if (data != null)
            {
                networking.SendBytes(data);
                // START_GAME request sent; no runtime log
            
                if (playButton != null)
                {
                    playButton.interactable = false;
                    TextMeshProUGUI buttonText = playButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "WAITING...";
                    }
                }
            
                AddChatMessage("System", GetMyUsername() + " is ready!");
            }
        }
    }

    private string GetMyUsername()
    {
        Networking networking = FindAnyObjectByType<Networking>();
        if (networking != null) return networking.username;
        return "";
    }

    private bool IsServer()
    {
        Networking networking = FindAnyObjectByType<Networking>();
        return networking != null && networking.mode == Networking.NetworkMode.Server;
    }

    private void UpdateInfo()
    {
        connectedPlayersText.text = "Connected Players: \n";
        foreach (string player in connectedPlayers)
        {
            connectedPlayersText.text += "- " + player + "\n";
        }

        roomInfoText.text = "Room: " + connectedPlayers.Count + " players";

        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        if (playButton != null)
        { 
            bool canStart = connectedPlayers.Count >= minPlayersToStart;
            playButton.interactable = canStart;

            TextMeshProUGUI buttonText = playButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                if (canStart)
                {
                    buttonText.text = "START GAME"; 
                }
                else
                {
                    buttonText.text = "WAITING " + connectedPlayers.Count+ " / " + minPlayersToStart;
                }
            }
        }
    }

    public void SendChatMessage()
    {
        if (!string.IsNullOrEmpty(chatMessageInput.text))
        {
            string message = chatMessageInput.text.Trim();
            SendChatToServer(message);
            chatMessageInput.text = "";
        }
    }

    private void SendChatToServer(string message)
    {
        Networking networking = FindAnyObjectByType<Networking>();
        if (networking != null) networking.SendChatMessage(message);
    }

    public void AddChatMessage(string sender, string message)
    {
        string formattedMessage;

        if (sender == "System")
        {
            formattedMessage = "<color=yellow>[System]</color>: " + message;
        }
        else
        {
            formattedMessage = "<color=purple>" + sender + "</color>: " + message;
        }

        if (chatMessages.Count > 0 && chatMessages[chatMessages.Count - 1] == formattedMessage)
        {
            return;
        }

        chatMessages.Add(formattedMessage);

        if (chatMessages.Count > 10)
        {
            chatMessages.RemoveAt(0);
        }

        chatText.text = string.Join("\n", chatMessages);
    }

    public void AddPlayer(string username)
    {
        if (!connectedPlayers.Contains(username))
        {
            connectedPlayers.Add(username);
            UpdateInfo();
            if (username != GetMyUsername())
            {
                AddChatMessage("System", username + " has joined the room.");
            }
        }
    }

    public void RemovePlayer(string username)
    {
        if (connectedPlayers.Remove(username))
        {
            UpdateInfo();
            AddChatMessage("System", username + " has left the room.");
        }
    }

    public void ClearPlayers()
    {
        connectedPlayers.Clear();
        UpdateInfo();
    }

    /// <summary>
    /// Synchronizes the local player list with the server's authoritative list
    /// </summary>
    public void SyncPlayerList(List<string> serverPlayerList)
    {
        // Get the local username to avoid duplicate notifications
        string myUsername = GetMyUsername();

        // Find players that left (in local but not in server list)
        List<string> playersToRemove = new List<string>();
        foreach (string localPlayer in connectedPlayers)
        {
            if (!serverPlayerList.Contains(localPlayer))
            {
                playersToRemove.Add(localPlayer);
            }
        }

        // Remove players that left
        foreach (string player in playersToRemove)
        {
            if (connectedPlayers.Remove(player))
            {
                if (player != myUsername)
                {
                    AddChatMessage("System", player + " has left the room.");
                }
            }
        }

        // Find new players (in server list but not in local)
        foreach (string serverPlayer in serverPlayerList)
        {
            if (!connectedPlayers.Contains(serverPlayer))
            {
                connectedPlayers.Add(serverPlayer);
                if (serverPlayer != myUsername)
                {
                    AddChatMessage("System", serverPlayer + " has joined the room.");
                }
            }
        }

        UpdateInfo();
    }
}
