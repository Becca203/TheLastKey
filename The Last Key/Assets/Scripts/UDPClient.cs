using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UDPClient : MonoBehaviour
{
    Socket clientSocket;
    IPEndPoint serverEndPoint;

    public string serverIP = "127.0.0.1";
    public string username = "User";
    int serverPort = 9050;

    private bool isRunning = true;
    private bool hasShutdown = false;

    private WaitingRoom waitingRoom;
    private bool shouldLoadWaitingRoom = false;
    private bool shouldLoadGameScene = false;
    private bool isInitialized = false;

    private string pendingPlayerList = "";
    private bool hasPendingPlayerList = false;

    void Start()
    {
        if (!isInitialized)
        {
            InitializeSocket();
            SendHandshake();
            isInitialized = true;
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (shouldLoadWaitingRoom)
        {
            shouldLoadWaitingRoom = false;
            SceneManager.LoadScene("WaitingRoom");
        }

        if (shouldLoadGameScene)
        {
            shouldLoadGameScene = false;
            SceneManager.LoadScene("GameScene");
        }

        if (hasPendingPlayerList && GetWaitingRoomManager() != null)
        {
            ProcessPlayerList(pendingPlayerList);
            pendingPlayerList = "";
            hasPendingPlayerList = false;
        }
    }

    private WaitingRoom GetWaitingRoomManager()
    {
        if (waitingRoom == null)
        {
            waitingRoom = FindAnyObjectByType<WaitingRoom>();
            if (waitingRoom != null && hasPendingPlayerList)
            {
                ProcessPlayerList(pendingPlayerList);
                pendingPlayerList = "";
                hasPendingPlayerList = false;
            }
        }
        return waitingRoom;
    }

    public void InitializeSocket()
    {
        if (clientSocket == null)
        {
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

            Thread receiveThread = new Thread(ReceiveMessages);
            receiveThread.Start();
        }
    }

    private void SendHandshake()
    {
        try
        {
            SendUsername();
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending handshake: " + e.Message);
        }
    }

    private void SendUsername()
    {
        string usernameMessage = "USERNAME:" + username;
        byte[] data = Encoding.ASCII.GetBytes(usernameMessage);
        clientSocket.SendTo(data, serverEndPoint);
        Debug.Log("Sent username to server: " + username);
    }

    public void SendChatMessage(string message)
    {
        try
        {
            string chatMessage = "CHAT:" + message;
            byte[] data = Encoding.ASCII.GetBytes(chatMessage);
            clientSocket.SendTo(data, serverEndPoint);
            Debug.Log("Sent chat message to server: " + message);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending chat message: " + e.Message);
        }
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[1024];
        EndPoint remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receiveBytes = clientSocket.ReceiveFrom(buffer, ref remoteEndPoint);

                string message = Encoding.ASCII.GetString(buffer, 0, receiveBytes);
                Debug.Log("Received from server: " + message);

                // Receives different types of messages
                if (message.StartsWith("SERVER_NAME:"))
                {
                    string serverName = message.Substring(12);
                    Debug.Log("Connected to server: " + serverName);
                    LoadWaitingRoom();
                }
                else if (message.StartsWith("PLAYER_LIST:"))
                {
                    string playerList = message.Substring(12);
                    if (GetWaitingRoomManager() == null)
                    {
                        pendingPlayerList = playerList;
                        hasPendingPlayerList = true;
                        Debug.Log("Pending player list stored: " + playerList);
                    }
                    else
                    {
                        ProcessPlayerList(playerList);
                    }
                }
                else if (message.StartsWith("PLAYER_JOINED:"))
                {
                    string playerName = message.Substring(14);
                    AddPlayerToRoom(playerName);
                }
                else if (message.StartsWith("PLAYER_LEFT:"))
                {
                    string playerName = message.Substring(12);
                    RemovePlayerFromRoom(playerName);
                }
                else if (message.StartsWith("CHAT:"))
                {
                    string chatMessage = message.Substring(5);
                    ProcessChatMessage(chatMessage);
                }
                else if (message.StartsWith("GAME_START:"))
                {
                    Debug.Log("Game is starting!");
                    shouldLoadGameScene = true;
                }
                else if (message == "ping")
                {
                    Debug.Log("Received ping from server");
                }
            }
            catch (SocketException se)
            {
                if (isRunning)
                {
                    Debug.Log("Socket error: " + se.Message);
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogError("Error receiving: " + e.Message);
                break;
            }
        }
    }

    private void RemovePlayerFromRoom(string playerName)
    {
        var waitingRoom = GetWaitingRoomManager();
        if (waitingRoom != null) waitingRoom.RemovePlayer(playerName);
    }

    private void AddPlayerToRoom(string playerName)
    {
        var waitingRoom = GetWaitingRoomManager();
        if (waitingRoom != null) waitingRoom.AddPlayer(playerName);
    }

    private void ProcessPlayerList(string playerList)
    {
        if (string.IsNullOrEmpty(playerList)) return;

        string[] players = playerList.Split(',');

        var waitingRoom = GetWaitingRoomManager();
        if (waitingRoom != null)
        {
            waitingRoom.ClearPlayers();
            foreach (string player in players)
            {
                if (!string.IsNullOrEmpty(player.Trim())) 
                    waitingRoom.AddPlayer(player.Trim());
            }
        }
    }

    private void ProcessChatMessage(string chatMessage)
    {
        int separatorIndex = chatMessage.IndexOf(':');
        if (separatorIndex > 0)
        {
            string sender = chatMessage.Substring(0, separatorIndex);
            string message = chatMessage.Substring(separatorIndex + 1);

            Debug.Log("From " + sender + ": " + message);

            var waitingRoom = GetWaitingRoomManager();
            if (waitingRoom != null) waitingRoom.AddChatMessage(sender, message);
        }
    }

    private void LoadWaitingRoom()
    {
        shouldLoadWaitingRoom = true;
    }

    // Cleanup on exit
    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;

        try { if (clientSocket != null) { clientSocket.Close(); } } catch { }
    }

    void OnApplicationQuit() => Shutdown();
    void OnDestroy() => Shutdown();
}
