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
    private bool shouldLoadGameOverScene = false; 

    private int assignedPlayerID = 0;
    private int winnerID = 0;
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
            waitingRoom = null; 
            SceneManager.LoadScene("WaitingRoom");
        }

        if (shouldLoadGameScene)
        {
            shouldLoadGameScene = false;
            SceneManager.LoadScene("GameScene");
        }

        if (shouldLoadGameOverScene)
        {
            shouldLoadGameOverScene = false;
            PlayerPrefs.SetInt("WinnerPlayerID", winnerID);
            PlayerPrefs.Save();
            SceneManager.LoadScene("GameOverScene");
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
            try
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                // Make reuse available and bind to ephemeral local port so ReceiveFrom works reliably
                clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                clientSocket.Bind(new IPEndPoint(IPAddress.Any, 0)); // OS assigns ephemeral port

                // Validate server IP and prepare endpoint
                try
                {
                    serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Invalid server IP: " + serverIP + " -> " + ex.Message);
                    return;
                }

                Debug.Log($"UDP Client initialized. Local endpoint: {clientSocket.LocalEndPoint}, Server: {serverEndPoint}");

                Thread receiveThread = new Thread(ReceiveMessages) { IsBackground = true };
                receiveThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError("Error initializing UDP client: " + e.Message);
            }
        }
    }

    // Send initial connection message to server
    private void SendHandshake()
    {
        if (clientSocket == null || serverEndPoint == null)
        {
            Debug.LogError("Cannot send handshake: socket or server endpoint not initialized");
            return;
        }

        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(msg);
            clientSocket.SendTo(data, serverEndPoint);
            Debug.Log("Sent username to server: " + username + " -> " + serverEndPoint);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending handshake: " + e.Message);
        }
    }

    // Send chat message to server
    public void SendChatMessage(string message)
    {
        if (clientSocket == null || serverEndPoint == null) return;
        ChatMessage chatMsg = new ChatMessage(username, message);
        byte[] data = NetworkSerializer.Serialize(chatMsg);
        if (data != null)
        {
            try { clientSocket.SendTo(data, serverEndPoint); }
            catch (Exception e) { Debug.LogError("Error sending chat: " + e.Message); }
        }
    }

    public void SendBytes(byte[] data)
    {
        if (clientSocket == null || serverEndPoint == null) return;
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
                    Debug.Log("Received message type: " + msgType + " from " + remoteEndPoint.ToString());
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
            case "GAME_OVER":
                ProcessGameOverMessage(buffer, length);
                break;
            case "UPDATE_KEY_STATE":
                ProcessUpdateKeyState(buffer, length);
                break;
            case "HIDE_KEY":
                ProcessHideKey(buffer, length);
                break;
            default:
                Debug.LogWarning("Unknown message type: " + msgType);
                break;
        }
    }

    private void ProcessGameOverMessage(byte[] buffer, int length)
    {
        SimpleMessage gameOverMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (gameOverMsg != null)
        {
            if (int.TryParse(gameOverMsg.content, out int id))
            {
                Debug.Log("Game over! Player " + id + " wins!");
                winnerID = id;
                shouldLoadGameOverScene = true;
            }
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
            int retries = 0;
            while (room == null && retries < 10)
            {
                Thread.Sleep(100); 
                room = GetWaitingRoomManager();
                retries++;
            }

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

    private void ProcessUpdateKeyState(byte[] buffer, int length)
    {
        SimpleMessage updateMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (updateMsg != null && int.TryParse(updateMsg.content, out int playerID))
        {
            Debug.Log("Player " + playerID + " has the key (sync)");
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                NetworkPlayer player = gameManager.FindPlayerByID(playerID);
                if (player != null)
                {
                    player.SetHasKey(true);
                }
            }
        }
    }
    private void ProcessHideKey(byte[] buffer, int length)
    {
        Debug.Log("Received HIDE_KEY message, disabling key object");
        KeyBehaviour key = FindAnyObjectByType<KeyBehaviour>();
        if (key != null)
        {
            key.gameObject.SetActive(false);
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