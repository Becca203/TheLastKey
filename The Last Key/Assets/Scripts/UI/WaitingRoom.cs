using System.Collections.Generic;
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

    // Data structures to store player and chat information
    private List<string> connectedPlayers = new List<string>();
    private List<string> chatMessages = new List<string>();

    private void Start()
    {
        AddChatMessage("System", "Welcome to the waiting room!");

        // If this is a client (not the server), add the player's username to the room
        if (!IsServer())
        {
            string myUsername = GetMyUsername();
            if (!string.IsNullOrEmpty(myUsername)) 
                AddPlayer(myUsername);
        }
        UpdateInfo();
    }

    private string GetMyUsername()
    {
        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        if (udpClient != null) return udpClient.username;
        return "";
    }

    private bool IsServer()
    {
        return FindAnyObjectByType<UDPServer>() != null;
    }

    private void UpdateInfo()
    {
        connectedPlayersText.text = "Connected Players: \n";
        foreach (string player in connectedPlayers)
        {
            connectedPlayersText.text += "- " + player + "\n";
        }

        roomInfoText.text = "Room: " + connectedPlayers.Count + " players";
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
        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        if (udpClient != null) udpClient.SendChatMessage(message);
    }

    public void AddChatMessage(string sender, string message)
    {
        chatMessages.Add(sender + ": " + message);
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
    }
}
