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

    // Queue for player list updates
    private PlayerListMessage pendingPlayerList = null;
    private bool hasPendingPlayerList = false;
    private object playerListLock = new object();

    // Queue for player joined/left messages
    private string pendingPlayerJoined = null;
    private string pendingPlayerLeft = null;
    private bool hasPendingPlayerJoined = false;
    private bool hasPendingPlayerLeft = false;
    private object playerUpdateLock = new object();

    // Queue for chat messages
    private ChatMessage pendingChatMessage = null;
    private bool hasPendingChatMessage = false;
    private object chatLock = new object();

    // Queue for key collected messages
    private int pendingKeyCollectedPlayerID = -1;
    private bool hasPendingKeyCollected = false;
    private object keyCollectedLock = new object();

    // Queue for hide key
    private bool shouldHideKey = false;
    private object hideKeyLock = new object();

    // Queue for key transfer messages
    private struct KeyTransferData
    {
        public int fromPlayerID;
        public int toPlayerID;
    }
    private KeyTransferData pendingKeyTransfer;
    private bool hasPendingKeyTransfer = false;
    private object keyTransferLock = new object();

    // Queue for push messages
    private struct PushData
    {
        public int playerID;
        public Vector2 velocity;
        public float duration;
    }
    private PushData pendingPush;
    private bool hasPendingPush = false;
    private object pushLock = new object();

    // Queue for scene load requests
    private string pendingSceneToLoad = null;
    private bool hasPendingSceneToLoad = false;
    private object sceneLoadLock = new object();
    private string nextLevelName = "";

    /// <summary>
    /// Manual initialization called by NetworkManager after setting serverIP and username
    /// </summary>
    public void Initialize()
    {
        if (isInitialized)
        {
            Debug.LogWarning("[CLIENT] Already initialized!");
            return;
        }

        InitializeSocket();
        SendHandshake();
        isInitialized = true;
        connectionTimer = 0f;
        waitingForServerResponse = true;
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

        // Process player list on main thread
        if (hasPendingPlayerList)
        {
            lock (playerListLock)
            {
                WaitingRoom room = GetWaitingRoomManager();
                if (room != null && pendingPlayerList != null)
                {
                    room.SyncPlayerList(pendingPlayerList.players);
                    hasPendingPlayerList = false;
                    pendingPlayerList = null;
                }
            }
        }

        // Process player joined on main thread
        if (hasPendingPlayerJoined)
        {
            lock (playerUpdateLock)
            {
                WaitingRoom room = GetWaitingRoomManager();
                if (room != null && !string.IsNullOrEmpty(pendingPlayerJoined))
                {
                    room.AddPlayer(pendingPlayerJoined);
                }
                hasPendingPlayerJoined = false;
                pendingPlayerJoined = null;
            }
        }

        // Process player left on main thread
        if (hasPendingPlayerLeft)
        {
            lock (playerUpdateLock)
            {
                WaitingRoom room = GetWaitingRoomManager();
                if (room != null && !string.IsNullOrEmpty(pendingPlayerLeft))
                {
                    room.RemovePlayer(pendingPlayerLeft);
                }
                hasPendingPlayerLeft = false;
                pendingPlayerLeft = null;
            }
        }

        // Process chat messages on main thread
        if (hasPendingChatMessage)
        {
            lock (chatLock)
            {
                WaitingRoom room = GetWaitingRoomManager();
                if (room != null && pendingChatMessage != null)
                {
                    room.AddChatMessage(pendingChatMessage.username, pendingChatMessage.message);
                }
                hasPendingChatMessage = false;
                pendingChatMessage = null;
            }
        }

        // Process key collected on main thread
        if (hasPendingKeyCollected)
        {
            lock (keyCollectedLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    NetworkPlayer player = gameManager.FindPlayerByID(pendingKeyCollectedPlayerID);
                    if (player != null)
                    {
                        player.SetHasKey(true);
                    }
                }
                hasPendingKeyCollected = false;
                pendingKeyCollectedPlayerID = -1;
            }
        }

        // Process hide key on main thread
        if (shouldHideKey)
        {
            lock (hideKeyLock)
            {
                KeyBehaviour key = FindAnyObjectByType<KeyBehaviour>();
                if (key != null)
                {
                    key.gameObject.SetActive(false);
                }
                shouldHideKey = false;
            }
        }

        // Process key transfer on main thread
        if (hasPendingKeyTransfer)
        {
            lock (keyTransferLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    NetworkPlayer fromPlayer = gameManager.FindPlayerByID(pendingKeyTransfer.fromPlayerID);
                    NetworkPlayer toPlayer = gameManager.FindPlayerByID(pendingKeyTransfer.toPlayerID);

                    if (fromPlayer != null && toPlayer != null)
                    {
                        fromPlayer.SetHasKey(false);
                        toPlayer.SetHasKey(true);
                    }
                }
                hasPendingKeyTransfer = false;
            }
        }

        // Process push on main thread
        if (hasPendingPush)
        {
            lock (pushLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    NetworkPlayer player = gameManager.FindPlayerByID(pendingPush.playerID);
                    if (player != null && !player.isLocalPlayer)
                    {
                        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                        if (rb != null)
                        {
                            rb.linearVelocity = pendingPush.velocity;
                            player.StartPush(pendingPush.duration);
                        }
                    }
                }
                hasPendingPush = false;
            }
        }

        // Process scene load requests on main thread
        if (hasPendingSceneToLoad)
        {
            lock (sceneLoadLock)
            {
                if (!string.IsNullOrEmpty(pendingSceneToLoad))
                {
                    Debug.Log($"[CLIENT] Loading scene: {pendingSceneToLoad}");
                    SceneManager.LoadScene(pendingSceneToLoad);
                }
                hasPendingSceneToLoad = false;
                pendingSceneToLoad = null;
            }
        }
    }

    public void InitializeSocket()
    {
        if (clientSocket != null)
        {
            Debug.LogWarning("[CLIENT] Socket already initialized");
            return;
        }

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
            case "KEY_COLLECTED":
                ProcessKeyCollectedMessage(buffer, length);
                break;
            case "HIDE_KEY":
                ProcessHideKey(buffer, length);
                break;
            case "KEY_TRANSFER":
                ProcessKeyTransferMessage(buffer, length);
                break;
            case "PUSH":
                ProcessPushMessage(buffer, length);
                break;
            case "LOAD_SCENE":
                ProcessLoadSceneMessage(buffer, length);
                break;
            case "LEVEL_COMPLETE":
                ProcessLevelCompleteMessage(buffer, length);
                break;
            default:
                Debug.LogWarning("[CLIENT] Unknown message type: " + msgType);
                break;
        }
    }

    private void ProcessUsernameMessage(byte[] buffer, int length)
    {
        SimpleMessage usernameMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (usernameMsg != null && usernameMsg.content.StartsWith("SERVER_NAME:"))
        {
            waitingForServerResponse = false;
            shouldLoadWaitingRoom = true;
        }
    }

    private void ProcessPlayerListMessage(byte[] buffer, int length)
    {
        PlayerListMessage playerListMsg = NetworkSerializer.Deserialize<PlayerListMessage>(buffer, length);
        if (playerListMsg == null) return;

        lock (playerListLock)
        {
            pendingPlayerList = playerListMsg;
            hasPendingPlayerList = true;
        }
    }

    private void ProcessPlayerJoinedMessage(byte[] buffer, int length)
    {
        SimpleMessage joinedMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (joinedMsg != null)
        {
            lock (playerUpdateLock)
            {
                pendingPlayerJoined = joinedMsg.content;
                hasPendingPlayerJoined = true;
            }
        }
    }

    private void ProcessPlayerLeftMessage(byte[] buffer, int length)
    {
        SimpleMessage leftMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (leftMsg != null)
        {
            lock (playerUpdateLock)
            {
                pendingPlayerLeft = leftMsg.content;
                hasPendingPlayerLeft = true;
            }
        }
    }

    private void ProcessChatMessage(byte[] buffer, int length)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
            lock (chatLock)
            {
                pendingChatMessage = chatMsg;
                hasPendingChatMessage = true;
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

    private void ProcessKeyCollectedMessage(byte[] buffer, int length)
    {
        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null && int.TryParse(keyMsg.content, out int playerID))
        {
            lock (keyCollectedLock)
            {
                pendingKeyCollectedPlayerID = playerID;
                hasPendingKeyCollected = true;
            }
        }
    }

    private void ProcessHideKey(byte[] buffer, int length)
    {
        lock (hideKeyLock)
        {
            shouldHideKey = true;
        }
    }

    private void ProcessKeyTransferMessage(byte[] buffer, int length)
    {
        KeyTransferMessage transferMsg = NetworkSerializer.Deserialize<KeyTransferMessage>(buffer, length);
        if (transferMsg != null)
        {
            lock (keyTransferLock)
            {
                pendingKeyTransfer = new KeyTransferData
                {
                    fromPlayerID = transferMsg.fromPlayerID,
                    toPlayerID = transferMsg.toPlayerID
                };
                hasPendingKeyTransfer = true;
            }
        }
    }

    private void ProcessPushMessage(byte[] buffer, int length)
    {
        PushMessage pushMsg = NetworkSerializer.Deserialize<PushMessage>(buffer, length);
        if (pushMsg != null)
        {
            lock (pushLock)
            {
                pendingPush = new PushData
                {
                    playerID = pushMsg.pushedPlayerID,
                    velocity = new Vector2(pushMsg.velocityX, pushMsg.velocityY),
                    duration = pushMsg.duration
                };
                hasPendingPush = true;
            }
        }
    }

    private void ProcessLoadSceneMessage(byte[] buffer, int length)
    {
        LoadSceneMessage sceneMsg = NetworkSerializer.Deserialize<LoadSceneMessage>(buffer, length);
        if (sceneMsg != null)
        {
            lock (sceneLoadLock)
            {
                pendingSceneToLoad = sceneMsg.sceneName;
                hasPendingSceneToLoad = true;
            }
        }
    }

    private void ProcessLevelCompleteMessage(byte[] buffer, int length)
    {
        SimpleMessage completeMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (completeMsg != null)
        {
            nextLevelName = completeMsg.content;
            LevelTransitionUI transitionUI = FindAnyObjectByType<LevelTransitionUI>();
            if (transitionUI != null)
            {
                transitionUI.ShowPanel();
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