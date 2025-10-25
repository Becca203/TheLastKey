using System;
using System.Net;
using System.Net.Sockets;
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
    private bool isInitialized = false;

    private WaitingRoom waitingRoom;
    private bool shouldLoadWaitingRoom = false;
    private bool shouldLoadGameScene = false;

    private int assignedPlayerID = 0;
    private bool shouldSetPlayerID = false;

    private struct PositionUpdate
    {
        public int playerID;
        public Vector3 position;
        public Vector2 velocity;
    }
    private PositionUpdate pendingPositionUpdate;
    private bool hasPendingPositionUpdate = false;
    private object positionLock = new object();

    void Start()
    {
        if (!isInitialized)
        {
            InitializeSocket();
            SendHandshake();
            isInitialized = true;
        }
    }

    // Ensure client persists across scenes
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    // Process pending updates from network thread
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

        if (shouldSetPlayerID)
        {
            shouldSetPlayerID = false;
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                gameManager.SetLocalPlayerID(assignedPlayerID);
            }
            else
            {
                Debug.LogWarning("GameManager not found, retrying...");
                shouldSetPlayerID = true;
            }
        }

        if (hasPendingPositionUpdate)
        {
            lock (positionLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    gameManager.UpdateRemotePlayerPosition(
                        pendingPositionUpdate.playerID,
                        pendingPositionUpdate.position,
                        pendingPositionUpdate.velocity
                    );
                }
                hasPendingPositionUpdate = false;
            }
        }
    }

    // Create UDP socket and start receive thread
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

    // Send initial connection message to server
    private void SendHandshake()
    {
        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(msg);
            clientSocket.SendTo(data, serverEndPoint);
            Debug.Log("Sent username to server: " + username);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending handshake: " + e.Message);
        }
    }

    // Send chat message to server
    public void SendChatMessage(string message)
    {
        ChatMessage chatMsg = new ChatMessage(username, message);
        byte[] data = NetworkSerializer.Serialize(chatMsg);
        if (data != null)
        {
            clientSocket.SendTo(data, serverEndPoint);
        }
    }

    public void SendBytes(byte[] data)
    {
        try
        {
            clientSocket.SendTo(data, serverEndPoint);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending bytes: " + e.Message);
        }
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receiveBytes = clientSocket.ReceiveFrom(buffer, ref remoteEndPoint);

                string msgType = NetworkSerializer.GetMessageType(buffer, receiveBytes);

                if (msgType != "POSITION")
                {
                    Debug.Log("Received message type: " + msgType);
                }

                ProcessMessage(msgType, buffer, receiveBytes);
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

    // Process message based on its type
    private void ProcessMessage(string msgType, byte[] buffer, int length)
    {
        switch (msgType)
        {
            case "USERNAME":
                ProcessUsernameMessage(buffer, length);
                break;
            case "PLAYER_LIST":
                ProcessPlayerListMessage(buffer, length);
                break;
            case "PLAYER_JOINED":
                ProcessPlayerJoinedMessage(buffer, length);
                break;
            case "PLAYER_LEFT":
                ProcessPlayerLeftMessage(buffer, length);
                break;
            case "CHAT":
                ProcessChatMessage(buffer, length);
                break;
            case "GAME_START":
                ProcessGameStartMessage(buffer, length);
                break;
            case "POSITION":
                ProcessPositionMessage(buffer, length);
                break;
            default:
                Debug.LogWarning("Unknown message type: " + msgType);
                break;
        }
    }

    private void ProcessUsernameMessage(byte[] buffer, int length)
    {
        SimpleMessage usernameMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (usernameMsg != null && usernameMsg.content.StartsWith("SERVER_NAME:"))
        {
            string serverName = usernameMsg.content.Substring(12);
            Debug.Log("Connected to server: " + serverName);
            shouldLoadWaitingRoom = true;
        }
    }

    private void ProcessPlayerListMessage(byte[] buffer, int length)
    {
        PlayerListMessage playerListMsg = NetworkSerializer.Deserialize<PlayerListMessage>(buffer, length);
        if (playerListMsg != null)
        {
            WaitingRoom room = GetWaitingRoomManager();
            if (room != null)
            {
                room.ClearPlayers();
                foreach (string player in playerListMsg.players)
                {
                    if (!string.IsNullOrEmpty(player.Trim()))
                        room.AddPlayer(player.Trim());
                }
            }
        }
    }

    private void ProcessPlayerJoinedMessage(byte[] buffer, int length)
    {
        SimpleMessage joinedMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (joinedMsg != null)
        {
            WaitingRoom room = GetWaitingRoomManager();
            if (room != null) room.AddPlayer(joinedMsg.content);
        }
    }

    private void ProcessPlayerLeftMessage(byte[] buffer, int length)
    {
        SimpleMessage leftMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (leftMsg != null)
        {
            WaitingRoom room = GetWaitingRoomManager();
            if (room != null) room.RemovePlayer(leftMsg.content);
        }
    }

    private void ProcessChatMessage(byte[] buffer, int length)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
            Debug.Log("From " + chatMsg.username + ": " + chatMsg.message);
            WaitingRoom room = GetWaitingRoomManager();
            if (room != null)
            {
                room.AddChatMessage(chatMsg.username, chatMsg.message);
            }
        }
    }

    private void ProcessGameStartMessage(byte[] buffer, int length)
    {
        GameStartMessage gameStartMsg = NetworkSerializer.Deserialize<GameStartMessage>(buffer, length);
        if (gameStartMsg != null)
        {
            assignedPlayerID = gameStartMsg.assignedPlayerID;
            shouldSetPlayerID = true;
            Debug.Log("Game starting! Assigned as Player " + assignedPlayerID);
            shouldLoadGameScene = true;
        }
    }

    private void ProcessPositionMessage(byte[] buffer, int length)
    {
        PositionMessage posMsg = NetworkSerializer.Deserialize<PositionMessage>(buffer, length);
        if (posMsg != null)
        {
            lock (positionLock)
            {
                pendingPositionUpdate = new PositionUpdate
                {
                    playerID = posMsg.playerID,
                    position = new Vector3(posMsg.posX, posMsg.posY, 0),
                    velocity = new Vector2(posMsg.velX, posMsg.velY)
                };
                hasPendingPositionUpdate = true;
            }
        }
    }

    private WaitingRoom GetWaitingRoomManager()
    {
        if (waitingRoom == null)
        {
            waitingRoom = FindAnyObjectByType<WaitingRoom>();
        }
        return waitingRoom;
    }

    // Close socket and cleanup
    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;
        try
        {
            if (clientSocket != null)
            {
                clientSocket.Close();
            }
        }
        catch { }
    }

    void OnApplicationQuit() => Shutdown();
    void OnDestroy() => Shutdown();
}