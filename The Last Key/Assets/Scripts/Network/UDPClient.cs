using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity.VisualScripting;
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
    private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool waitingForServerResponse = false;

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

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (!isInitialized)
        {
            InitializeSocket();
            SendHandshake();
            isInitialized = true;
            connectionTimer = 0f;
        }
    }

    private void Update()
    {
        // Connection timeout check
        if (waitingForServerResponse)
        {
            connectionTimer += Time.deltaTime;
            if (connectionTimer > connectionTimeout)
            {
                Debug.LogError($"[CLIENT] Connection timeout! No response from server at {serverIP}:{serverPort}");
                waitingForServerResponse = false;
            }
        }

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

    public void InitializeSocket()
    {
        if (clientSocket != null) return;

        try
        {
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 0);
            clientSocket.Bind(localEndPoint);

            IPAddress serverAddr = IPAddress.Parse(serverIP);
            serverEndPoint = new IPEndPoint(serverAddr, serverPort);

            Thread receiveThread = new Thread(ReceiveMessages)
            {
                IsBackground = true,
                Name = "UDP_Client_Receive"
            };
            receiveThread.Start();
            Debug.Log($"[CLIENT] Connected to {serverEndPoint}");
        }
        catch (Exception e)
        {
           Debug.LogError($"[CLIENT] Socket initialization failed: {e.Message}");
        }
    }

    private void SendHandshake()
    {
        if (clientSocket == null || serverEndPoint == null)
        {
            Debug.LogError("[CLIENT] Cannot send handshake: socket or server endpoint not initialized");
            return;
        }

        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(msg);

            if (data != null)
            {
                clientSocket.SendTo(data, serverEndPoint);
                Debug.LogError($"[CLIENT] Handshake sent: {username}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CLIENT] Error sending handshake: {e.Message}");
        }
    }

    public void SendChatMessage(string message)
    {
        if (clientSocket == null || serverEndPoint == null) return;

        ChatMessage chatMsg = new ChatMessage(username, message);
        byte[] data = NetworkSerializer.Serialize(chatMsg);

        if (data != null)
        {
            try { clientSocket.SendTo(data, serverEndPoint); }
            catch (Exception e) { Debug.LogError("[CLIENT] Error sending chat: " + e.Message); }
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
            Debug.LogError("[CLIENT] Error sending bytes: " + e.Message);
        }
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receiveBytes = clientSocket.ReceiveFrom(buffer, ref remoteEndPoint);
                string msgType = NetworkSerializer.GetMessageType(buffer, receiveBytes);

                if (msgType != "POSITION")
                {
                    Debug.Log($"[CLIENT] <<< Received {msgType} ({receiveBytes} bytes) from {remoteEndPoint}");
                }

                ProcessMessage(msgType, buffer, receiveBytes);
            }
            catch (SocketException se)
            {
                if (isRunning)
                {
                    Debug.LogError($"[CLIENT] Socket error: {se.ErrorCode} - {se.Message}");
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (Exception e)
            {
                if (isRunning)
                {
                    Debug.LogError($"[CLIENT] Receive error: {e.Message}");
                }
            }
        }
    }

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
                Debug.LogWarning("[CLIENT] Unknown message type: " + msgType);
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
                Debug.Log("[CLIENT] Game over! Player " + id + " wins!");
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
            Debug.Log("[CLIENT] Connected to server: " + serverName);
            waitingForServerResponse = false;
            shouldLoadWaitingRoom = true;
        }
    }

    private void ProcessPlayerListMessage(byte[] buffer, int length)
    {
        PlayerListMessage playerListMsg = NetworkSerializer.Deserialize<PlayerListMessage>(buffer, length);
        if (playerListMsg != null) return;

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
            Debug.Log("[CLIENT] From " + chatMsg.username + ": " + chatMsg.message);
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
            Debug.Log("[CLIENT] Game starting! Assigned as Player " + assignedPlayerID);
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
            Debug.Log("[CLIENT] Player " + playerID + " has the key (sync)");
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
        Debug.Log("[CLIENT] Received HIDE_KEY message, disabling key object");
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

    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;
        Debug.Log("[CLIENT] Shutting down...");
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