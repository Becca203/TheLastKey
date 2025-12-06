using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Networking : MonoBehaviour
{
    public enum NetworkMode
    {
        None,
        Client,
        Server
    }

    [Header("Network Configuration")]
    public NetworkMode mode = NetworkMode.None;
    public string serverIP = "127.0.0.1";
    public string username = "User";
    public int serverPort = 9050;

    [Header("Ping Settings")]
    [SerializeField] private float pingInterval = 2f;
    [SerializeField] private float disconnectionTimeout = 30f;

    [Header("Replication")]
    private ReplicationManager replicationManager;

    // Socket
    private Socket socket;
    private IPEndPoint serverEndPoint;
    private bool isRunning = true;
    private bool hasShutdown = false;
    private bool isInitialized = false;

    // Connection state
    private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool waitingForServerResponse = false;
    private float lastPingTime = 0f;
    private DateTime lastPingReceived = DateTime.Now;

    // Server-specific data
    private string serverName = "GameServer";
    private List<ClientProxy> connectedClients = new List<ClientProxy>();
    private object clientsLock = new object();
    private bool gameStarted = false;

    // Key state management (server-authoritative)
    private bool keyCollected = false;
    private int keyOwnerPlayerID = -1;
    private object keyStateLock = new object();

    // Level transition
    private Dictionary<int, bool> levelTransitionVotes = new Dictionary<int, bool>();
    private object votesLock = new object();
    private string nextLevelName = "";

    // Client-specific UI loading flags
    private WaitingRoom waitingRoom;
    private bool shouldLoadWaitingRoom = false;
    private bool shouldLoadGameScene = false;
    private int assignedPlayerID = 0;
    private bool shouldSetPlayerID = false;

    // Message queues for main thread processing (client side)
    private PositionUpdate pendingPositionUpdate;
    private bool hasPendingPositionUpdate = false;
    private object positionLock = new object();

    private PlayerListMessage pendingPlayerList = null;
    private bool hasPendingPlayerList = false;
    private object playerListLock = new object();

    private string pendingPlayerJoined = null;
    private string pendingPlayerLeft = null;
    private bool hasPendingPlayerJoined = false;
    private bool hasPendingPlayerLeft = false;
    private object playerUpdateLock = new object();

    private ChatMessage pendingChatMessage = null;
    private bool hasPendingChatMessage = false;
    private object chatLock = new object();

    private int pendingKeyCollectedPlayerID = -1;
    private bool hasPendingKeyCollected = false;
    private object keyCollectedLock = new object();

    private bool shouldHideKey = false;
    private object hideKeyLock = new object();

    private KeyTransferData pendingKeyTransfer;
    private bool hasPendingKeyTransfer = false;
    private object keyTransferLock = new object();

    private PushData pendingPush;
    private bool hasPendingPush = false;
    private object pushLock = new object();

    private string pendingSceneToLoad = null;
    private bool hasPendingSceneToLoad = false;
    private object sceneLoadLock = new object();

    // NUEVA COLA PARA LEVEL COMPLETE
    private bool shouldShowLevelTransitionUI = false;
    private object levelCompleteLock = new object();

    private bool hasStarted = false;

    // Structs
    private struct PositionUpdate
    {
        public int playerID;
        public Vector3 position;
        public Vector2 velocity;
    }

    private struct KeyTransferData
    {
        public int fromPlayerID;
        public int toPlayerID;
    }

    private struct PushData
    {
        public int playerID;
        public Vector2 velocity;
        public float duration;
    }

    private class ClientProxy
    {
        public IPEndPoint endpoint;
        public string username;
        public int playerID;
        public DateTime lastPingTime;

        public ClientProxy(IPEndPoint ep, string user, int id)
        {
            endpoint = ep;
            username = user;
            playerID = id;
            lastPingTime = DateTime.Now;
        }
    }

    // Initialization
    public void Initialize(NetworkMode networkMode, string ip = "127.0.0.1", string user = "Player")
    {
        if (isInitialized)
        {
            Debug.LogWarning("[NETWORK] Already initialized!");
            return;
        }

        mode = networkMode;
        serverIP = ip;
        username = user;
        
        Debug.Log($"[NETWORK] Initialize() called - Mode: {mode}, IP: {ip}, User: {user}");
    }

    void Start()
    {
        if (hasStarted)
        {
            Debug.LogWarning("[NETWORK] Start() already called, skipping");
            return;
        }
        hasStarted = true;

        if (mode == NetworkMode.None)
        {
            Debug.LogWarning("[NETWORK] Mode not set. Call Initialize() first.");
            return;
        }

        Debug.Log($"[NETWORK] Starting in {mode} mode...");

        // Initialize ReplicationManager
        replicationManager = gameObject.AddComponent<ReplicationManager>();
        if (replicationManager != null)
        {
            replicationManager.Initialize(mode == NetworkMode.Server);
            Debug.Log("[NETWORK] ReplicationManager initialized");
        }
        else
        {
            Debug.LogError("[NETWORK] Failed to create ReplicationManager!");
        }

        CreateAndBindSocket();

        if (socket == null)
        {
            Debug.LogError("[NETWORK] Failed to create socket! Check console for errors.");
            return;
        }

        if (mode == NetworkMode.Client)
        {
            Debug.Log("[NETWORK] Sending handshake to server...");
            SendHandshake();
            connectionTimer = 0f;
            waitingForServerResponse = true;
        }
        else if (mode == NetworkMode.Server)
        {
            Debug.Log($"[SERVER] Successfully started and listening on port {serverPort}");
        }

        isInitialized = true;
        lastPingTime = Time.time;
        lastPingReceived = DateTime.Now;
        
        Debug.Log($"[NETWORK] Initialization complete - isInitialized: {isInitialized}");
    }

    private void CreateAndBindSocket()
    {
        try
        {
            Debug.Log($"[NETWORK] Creating socket for {mode} mode...");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            try
            {
                socket.ExclusiveAddressUse = false;
            }
            catch { }

            if (mode == NetworkMode.Server)
            {
                try
                {
                    IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, serverPort);
                    socket.Bind(localEndPoint);

                    // Specific config for server socket
                    socket.Blocking = true;

                    Debug.Log($"[SERVER] Socket bound to Port {serverPort}. Listening on all IPs.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SERVER] Failed to bind socket: {e.Message}");
                    Debug.LogError($"[SERVER] Stack trace: {e.StackTrace}");
                    try 
                    { 
                        socket.Close(); 
                        socket.Dispose(); 
                    } 
                    catch { }
                    socket = null;
                    ReportError($"Failed to bind server socket: {e.Message}");
                    return;
                }
            }
            else if (mode == NetworkMode.Client)
            {
                // Bind client to any available port
                try
                {
                    IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    socket.Bind(clientEndPoint);
                    Debug.Log($"[CLIENT] Bound to local port {((IPEndPoint)socket.LocalEndPoint).Port}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CLIENT] Failed to bind socket: {e.Message}");
                    ReportError($"Failed to bind client socket: {e.Message}");
                    socket?.Close();
                    socket = null;
                    return;
                }

                socket.Blocking = true;
                
                try
                {
                    IPAddress serverAddr = IPAddress.Parse(serverIP);
                    serverEndPoint = new IPEndPoint(serverAddr, serverPort);
                    Debug.Log($"[CLIENT] Server endpoint set to {serverIP}:{serverPort}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CLIENT] Failed to parse server IP: {e.Message}");
                    socket?.Close();
                    socket = null;
                    return;
                }
            }

            // Start receive thread
            Thread receiveThread = new Thread(ReceiveMessages)
            {
                IsBackground = true,
                Name = $"UDP_Receive_Thread_{mode}"
            };
            receiveThread.Start();

            Debug.Log($"[NETWORK] Socket created successfully in {mode} mode, receive thread started");
        }
        catch (Exception e)
        {
            Debug.LogError($"[NETWORK] Socket creation failed: {e.Message}");
            Debug.LogError($"[NETWORK] Stack trace: {e.StackTrace}");
            ReportError($"Socket creation failed: {e.Message}");
            socket = null;
        }
    }

    // Main update loop
    void Update()
    {
        OnUpdate();
    }

    void OnUpdate()
    {
        if (!isInitialized) return;

        if (Time.time - lastPingTime >= pingInterval)
        {
            SendPing();
            lastPingTime = Time.time;
        }

        if (mode == NetworkMode.Client)
        {
            HandleClientUpdate();
        }
        else if (mode == NetworkMode.Server)
        {
            HandleServerUpdate();
        }

        ProcessMessageQueues();
    }

    private void HandleClientUpdate()
    {
        if (waitingForServerResponse)
        {
            connectionTimer += Time.deltaTime;
            if (connectionTimer > connectionTimeout)
            {
                Debug.LogError($"[CLIENT] Connection timeout! No response from server at {serverIP}:{serverPort}");
                waitingForServerResponse = false;
            }
        }

        double timeSinceLastPing = (DateTime.Now - lastPingReceived).TotalSeconds;
        if (timeSinceLastPing > disconnectionTimeout)
        {
            Debug.LogError("[CLIENT] Server connection lost!");
            OnConnectionReset(serverEndPoint);
            lastPingReceived = DateTime.Now;
        }
    }

    private void HandleServerUpdate()
    {
        lock (clientsLock)
        {
            List<ClientProxy> clientsToRemove = new List<ClientProxy>();

            foreach (ClientProxy client in connectedClients)
            {
                double timeSinceLastPing = (DateTime.Now - client.lastPingTime).TotalSeconds;
                if (timeSinceLastPing > disconnectionTimeout)
                {
                    Debug.Log($"[SERVER] Client {client.username} timed out");
                    clientsToRemove.Add(client);
                }
            }

            foreach (ClientProxy client in clientsToRemove)
            {
                OnConnectionReset(client.endpoint);
                connectedClients.Remove(client);

                SimpleMessage leftMsg = new SimpleMessage("PLAYER_LEFT", client.username);
                BroadcastMessage(leftMsg);
            }
        }
    }

    private void SendPing()
    {
        SimpleMessage pingMsg = new SimpleMessage("PING", "");
        byte[] data = NetworkSerializer.Serialize(pingMsg);

        if (mode == NetworkMode.Client && serverEndPoint != null)
        {
            SendPacket(data, serverEndPoint);
        }
        else if (mode == NetworkMode.Server)
        {
            lock (clientsLock)
            {
                foreach (ClientProxy client in connectedClients)
                {
                    SendPacket(data, client.endpoint);
                }
            }
        }
    }

    private void ProcessMessageQueues()
    {
        // Scene loading
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

        // Player ID assignment
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

        // Position updates
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

        // Player list
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

        // Player joined
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

        // Player left
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

        // Chat messages
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

        // Key collected
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
                        Debug.Log($"[NETWORK] Main thread: Player {pendingKeyCollectedPlayerID} set hasKey=true");
                    }
                }
                hasPendingKeyCollected = false;
                pendingKeyCollectedPlayerID = -1;
            }
        }

        // Hide key - AHORA SE PROCESA EN EL MAIN THREAD
        if (shouldHideKey)
        {
            lock (hideKeyLock)
            {
                KeyBehaviour key = FindAnyObjectByType<KeyBehaviour>();
                if (key != null && key.gameObject.activeSelf)
                {
                    key.gameObject.SetActive(false);
                    Debug.Log("[NETWORK] Main thread: Key hidden");
                }
                shouldHideKey = false;
            }
        }

        // Key transfer
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
                        Debug.Log($"[NETWORK] Main thread: Key transferred from {pendingKeyTransfer.fromPlayerID} to {pendingKeyTransfer.toPlayerID}");
                    }
                }
                hasPendingKeyTransfer = false;
            }
        }

        // Push
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
                            Debug.Log($"[NETWORK] Main thread: Player {pendingPush.playerID} pushed");
                        }
                    }
                }
                hasPendingPush = false;
            }
        }

        // Level complete UI - NUEVA COLA PROCESADA EN MAIN THREAD
        if (shouldShowLevelTransitionUI)
        {
            lock (levelCompleteLock)
            {
                LevelTransitionUI transitionUI = FindAnyObjectByType<LevelTransitionUI>();
                if (transitionUI != null)
                {
                    transitionUI.ShowPanel();
                    Debug.Log("[NETWORK] Main thread: Showing level transition UI");
                }
                shouldShowLevelTransitionUI = false;
            }
        }

        // Scene loading
        if (hasPendingSceneToLoad)
        {
            lock (sceneLoadLock)
            {
                if (!string.IsNullOrEmpty(pendingSceneToLoad))
                {
                    Debug.Log($"[NETWORK] Loading scene: {pendingSceneToLoad}");
                    if (Time.timeScale == 0f) Time.timeScale = 1f;

                    bool isReturningToMenu = (pendingSceneToLoad == "MainMenu");
                    SceneManager.LoadScene(pendingSceneToLoad);

                    if (isReturningToMenu)
                    {
                        if (NetworkManager.Instance != null)
                            NetworkManager.Instance.ResetNetwork();

                        if (GameManager.Instance != null)
                            Destroy(GameManager.Instance.gameObject);
                    }
                }
                hasPendingSceneToLoad = false;
                pendingSceneToLoad = null;
            }
        }
    }

    // Package handling
    void ReceiveMessages()
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                if (socket == null) break;

                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = socket.ReceiveFrom(buffer, ref remoteEndPoint);

                OnPacketReceived(buffer, receivedBytes, (IPEndPoint)remoteEndPoint);
            }
            catch (SocketException se)
            {
                if (!isRunning) break;

                if (se.SocketErrorCode == SocketError.WouldBlock)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue;
                }

                if (se.SocketErrorCode != SocketError.Interrupted)
                {
                    Debug.LogError($"[NETWORK] Socket error: {se.ErrorCode} - {se.Message}");
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
                    Debug.LogError($"[NETWORK] Receive error: {e.Message}");
                }
            }
        }
    }

    void OnPacketReceived(byte[] buffer, int length, IPEndPoint fromAddress)
    {
        string msgType = NetworkSerializer.GetMessageType(buffer, length);

        if (mode == NetworkMode.Server)
        {
            ClientProxy client = GetOrCreateClient(fromAddress);
            ProcessServerMessage(msgType, buffer, length, client);
        }
        else if (mode == NetworkMode.Client)
        {
            ProcessClientMessage(msgType, buffer, length);
        }
    }

    //  Client message processing
    private void ProcessClientMessage(string msgType, byte[] buffer, int length)
    {
        switch (msgType)
        {
            case "PING":
                lastPingReceived = DateTime.Now;
                break;
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
            case "LEVEL_TRANSITION":
                Debug.Log("[CLIENT] Received LEVEL_TRANSITION message (server-side only, ignoring)");
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
            Debug.Log($"[CLIENT] Received GAME_START - Assigned Player ID: {gameStartMsg.assignedPlayerID}");
            
            assignedPlayerID = gameStartMsg.assignedPlayerID;
            shouldSetPlayerID = true;
            shouldLoadGameScene = true;
            
            Debug.Log($"[CLIENT] Flags set - shouldLoadGameScene: {shouldLoadGameScene}, shouldSetPlayerID: {shouldSetPlayerID}");
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
                Debug.Log($"[NETWORK] Thread: Queued KEY_COLLECTED for player {playerID}");
            }
        }
    }

    private void ProcessHideKey(byte[] buffer, int length)
    {
        lock (hideKeyLock)
        {
            shouldHideKey = true;
            Debug.Log("[NETWORK] Thread: Queued HIDE_KEY");
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
                Debug.Log($"[NETWORK] Thread: Queued KEY_TRANSFER from {transferMsg.fromPlayerID} to {transferMsg.toPlayerID}");
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
                Debug.Log($"[NETWORK] Thread: Queued PUSH for player {pushMsg.pushedPlayerID}");
            }
        }
    }

    private void ProcessLoadSceneMessage(byte[] buffer, int length)
    {
        LoadSceneMessage sceneMsg = NetworkSerializer.Deserialize<LoadSceneMessage>(buffer, length);
        if (sceneMsg != null && !string.IsNullOrEmpty(sceneMsg.sceneName))
        {
            Debug.Log($"[CLIENT] Received LOAD_SCENE: {sceneMsg.sceneName}");
            
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
            
            // NO USAR FindAnyObjectByType AQUÍ - usar cola
            lock (levelCompleteLock)
            {
                shouldShowLevelTransitionUI = true;
                Debug.Log($"[NETWORK] Thread: Queued LEVEL_COMPLETE for next level: {nextLevelName}");
            }
        }
    }

    // Server message processing
    private void ProcessServerMessage(string msgType, byte[] buffer, int length, ClientProxy client)
    {
        if (client == null) return;
        
        client.lastPingTime = DateTime.Now;

        switch (msgType)
        {
            case "PING":
                break;
            case "USERNAME":
                ProcessServerUsernameMessage(buffer, length, client);
                break;
            case "CHAT":
                ProcessServerChatMessage(buffer, length, client);
                break;
            case "POSITION":
                ProcessServerPositionMessage(buffer, length, client);
                break;
            case "START_GAME":
                ProcessStartGameRequest();
                break;
            case "KEY_COLLECTED":
                ProcessServerKeyCollectedMessage(buffer, length, client);
                break;
            case "KEY_TRANSFER":
                ProcessServerKeyTransferMessage(buffer, length);
                break;
            case "PUSH":
                ProcessServerPushMessage(buffer, length);
                break;
            case "LEVEL_TRANSITION":
                ProcessLevelTransitionMessage(buffer, length, client);
                break;
            case "LEVEL_COMPLETE":
                ProcessServerLevelCompleteMessage(buffer, length);
                break;
            default:
                Debug.LogWarning("[SERVER] Unknown message type: " + msgType);
                break;
        }
    }

    private ClientProxy GetOrCreateClient(IPEndPoint endpoint)
    {
        lock (clientsLock)
        {
            foreach (ClientProxy client in connectedClients)
            {
                if (client.endpoint.Address.Equals(endpoint.Address) && client.endpoint.Port == endpoint.Port)
                {
                    return client;
                }
            }

            if (connectedClients.Count >= 2)
            {
                Debug.LogWarning($"[SERVER] Max clients (2) already connected, rejecting {endpoint}");
                return null;
            }

            ClientProxy newClient = new ClientProxy(
                endpoint,
                "",
                connectedClients.Count + 1
            );
            connectedClients.Add(newClient);
            Debug.Log($"[SERVER] New client connected from {endpoint} (Total: {connectedClients.Count})");

            return newClient;
        }
    }

    private void ProcessServerUsernameMessage(byte[] buffer, int length, ClientProxy client)
    {
        SimpleMessage usernameMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (usernameMsg != null)
        {
            if (client.username != usernameMsg.content)
            {
                client.username = usernameMsg.content;

                SendServerName(client.endpoint);
                SendUserList();

                SimpleMessage joinedMsg = new SimpleMessage("PLAYER_JOINED", usernameMsg.content);
                BroadcastMessage(joinedMsg, client);
            }
        }
    }

    private void SendServerName(IPEndPoint clientEndpoint)
    {
        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", "SERVER_NAME:" + serverName);
            byte[] data = NetworkSerializer.Serialize(msg);
            SendPacket(data, clientEndpoint);
        }
        catch (Exception e)
        {
            ReportError($"Error sending server name: {e.Message}");
        }
    }

    private void SendUserList()
    {
        lock (clientsLock)
        {
            PlayerListMessage playerListMsg = new PlayerListMessage();
            foreach (ClientProxy client in connectedClients)
            {
                if (!string.IsNullOrEmpty(client.username))
                {
                    playerListMsg.players.Add(client.username);
                }
            }

            byte[] data = NetworkSerializer.Serialize(playerListMsg);
            foreach (ClientProxy client in connectedClients)
            {
                try
                {
                    SendPacket(data, client.endpoint);
                }
                catch (Exception e)
                {
                    ReportError($"Error sending user list: {e.Message}");
                }
            }
        }
    }

    private void ProcessServerChatMessage(byte[] buffer, int length, ClientProxy client)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
            BroadcastMessage(chatMsg, null);

            if (mode == NetworkMode.Server && client != null)
            {
                lock (chatLock)
                {
                    pendingChatMessage = chatMsg;
                    hasPendingChatMessage = true;
                }
            }
        }
    }

    private void ProcessServerPositionMessage(byte[] buffer, int length, ClientProxy sender)
    {
        if (!gameStarted) return;
    
        PositionMessage posMsg = NetworkSerializer.Deserialize<PositionMessage>(buffer, length);
        if (posMsg == null) return;

        // Update sender's last ping time
        lock (clientsLock)
        {
            sender.lastPingTime = DateTime.Now;
        }

        // IMPORTANTE: Broadcast a TODOS los demás clientes
        BroadcastMessage(posMsg, sender);
    }

    private void ProcessStartGameRequest()
    {
        lock (clientsLock)
        {
            HashSet<string> uniqueUsernames = new HashSet<string>();
            foreach (ClientProxy c in connectedClients)
            {
                if (!string.IsNullOrEmpty(c.username))
                    uniqueUsernames.Add(c.username);
            }
            int uniqueClients = uniqueUsernames.Count;

            Debug.Log($"[SERVER] ProcessStartGameRequest - Connected clients: {connectedClients.Count}, Unique usernames: {uniqueClients}");

            if (uniqueClients >= 2)
            {
                if (gameStarted)
                {
                    Debug.LogWarning("[SERVER] Game already started, ignoring duplicate START_GAME request");
                    return;
                }

                gameStarted = true;
                Debug.Log("[SERVER] Starting game with " + uniqueClients + " players");

                // Reset key state for new game
                lock (keyStateLock)
                {
                    keyCollected = false;
                    keyOwnerPlayerID = -1;
                    Debug.Log("[SERVER] Key state reset for new game");
                }

                // Assign player IDs and send GAME_START to all clients
                int playerID = 1;
                foreach (ClientProxy client in connectedClients)
                {
                    client.playerID = playerID;
                    
                    GameStartMessage startMsg = new GameStartMessage(playerID, uniqueClients);
                    byte[] data = NetworkSerializer.Serialize(startMsg);
                    
                    if (data != null)
                    {
                        Debug.Log($"[SERVER] Sending GAME_START to {client.username} at {client.endpoint} (Player {playerID})");
                        SendPacket(data, client.endpoint);
                        Debug.Log($"[SERVER] GAME_START sent successfully to {client.username}");
                    }
                    else
                    {
                        Debug.LogError($"[SERVER] Failed to serialize GAME_START for {client.username}");
                    }
                    
                    playerID++;
                }
            }
            else
            {
                Debug.LogWarning("[SERVER] Not enough players to start (" + uniqueClients + "/2)");
            }
        }
    }

    private void ProcessServerKeyCollectedMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null)
        {
            Debug.LogError("[SERVER] ProcessServerKeyCollectedMessage called with null client!");
            return;
        }

        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null && replicationManager != null)
        {
            if (int.TryParse(keyMsg.content, out int playerID))
            {
                Debug.Log($"[SERVER] Received KEY_COLLECTED request from Player {playerID} (client: {client.username})");
                
                // SERVER-SIDE VALIDATION
                lock (keyStateLock)
                {
                    if (keyCollected)
                    {
                        // Key already collected, reject
                        Debug.LogWarning($"[SERVER] REJECTED: Player {playerID} tried to collect key, but Player {keyOwnerPlayerID} already has it!");
                        
                        // Send correction to the client that tried to collect it
                        SendKeyRejection(client.endpoint, keyOwnerPlayerID);
                        return;
                    }

                    // Key available, authorize collection
                    keyCollected = true;
                    keyOwnerPlayerID = playerID;
                    Debug.Log($"[SERVER] AUTHORIZED: Player {playerID} collected key successfully!");
                }

                // Replicate to all clients
                Debug.Log($"[SERVER] Broadcasting key collection to all clients");
                replicationManager.ReplicateKeyCollection(playerID);
            }
            else
            {
                Debug.LogError($"[SERVER] Failed to parse playerID from KEY_COLLECTED message: {keyMsg.content}");
            }
        }
    }

    private void SendKeyRejection(IPEndPoint clientEndpoint, int actualOwnerID)
    {
        // Force hide key locally
        SimpleMessage hideKeyMsg = new SimpleMessage("HIDE_KEY", "");
        byte[] hideData = NetworkSerializer.Serialize(hideKeyMsg);
        if (hideData != null)
        {
            SendPacket(hideData, clientEndpoint);
        }

        // Send correct state of who has the key
        SimpleMessage keyStateMsg = new SimpleMessage("KEY_COLLECTED", actualOwnerID.ToString());
        byte[] keyData = NetworkSerializer.Serialize(keyStateMsg);
        if (keyData != null)
        {
            SendPacket(keyData, clientEndpoint);
        }

        Debug.Log($"[SERVER] Sent key rejection to client, correct owner is Player {actualOwnerID}");
    }

    private void ProcessServerKeyTransferMessage(byte[] buffer, int length)
    {
        KeyTransferMessage transferMsg = NetworkSerializer.Deserialize<KeyTransferMessage>(buffer, length);
        if (transferMsg != null && replicationManager != null)
        {
            // VALIDATION: can only transfer if actually has the key
            lock (keyStateLock)
            {
                if (!keyCollected || keyOwnerPlayerID != transferMsg.fromPlayerID)
                {
                    Debug.LogWarning($"[SERVER] Player {transferMsg.fromPlayerID} tried to transfer key but doesn't have it. REJECTED.");
                    return;
                }

                // Transfer the key
                keyOwnerPlayerID = transferMsg.toPlayerID;
                Debug.Log($"[SERVER] Key transfer from {transferMsg.fromPlayerID} to {transferMsg.toPlayerID} (AUTHORIZED), replicating");
            }

            replicationManager.ReplicateKeyTransfer(transferMsg.fromPlayerID, transferMsg.toPlayerID);
        }
    }

    private void ProcessServerPushMessage(byte[] buffer, int length)
    {
        PushMessage pushMsg = NetworkSerializer.Deserialize<PushMessage>(buffer, length);
        if (pushMsg != null && replicationManager != null)
        {
            Debug.Log($"[SERVER] Player {pushMsg.pushedPlayerID} pushed, replicating");
            replicationManager.ReplicatePush(
                pushMsg.pushedPlayerID,
                new Vector2(pushMsg.velocityX, pushMsg.velocityY),
                pushMsg.duration
            );
        }
    }

    private void ProcessLevelTransitionMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null)
        {
            Debug.LogError("[SERVER] ProcessLevelTransitionMessage called with null client!");
            return;
        }

        LevelTransitionMessage transitionMsg = NetworkSerializer.Deserialize<LevelTransitionMessage>(buffer, length);
        if (transitionMsg != null)
        {
            Debug.Log($"[SERVER] Received vote from client {client.username} (endpoint: {client.endpoint})");
            
            lock (votesLock)
            {
                // Verificar si este jugador ya votó
                if (levelTransitionVotes.ContainsKey(transitionMsg.playerID))
                {
                    Debug.LogWarning($"[SERVER] Player {transitionMsg.playerID} already voted! Updating vote.");
                }
                
                levelTransitionVotes[transitionMsg.playerID] = transitionMsg.wantsToContinue;
                Debug.Log($"[SERVER] Player {transitionMsg.playerID} voted: {(transitionMsg.wantsToContinue ? "Continue" : "Return")}");

                // Debug: mostrar todos los votos actuales
                string votesDebug = "[SERVER] Current votes: ";
                foreach (var kvp in levelTransitionVotes)
                {
                    votesDebug += $"Player{kvp.Key}={kvp.Value}, ";
                }
                Debug.Log(votesDebug);

                int totalPlayers = 2;
                Debug.Log($"[SERVER] Votes received: {levelTransitionVotes.Count}/{totalPlayers}");

                if (levelTransitionVotes.Count >= totalPlayers)
                {
                    bool allWantToContinue = true;

                    foreach (var vote in levelTransitionVotes.Values)
                    {
                        if (!vote)
                        {
                            allWantToContinue = false;
                            break;
                        }
                    }

                    string targetScene = allWantToContinue ? nextLevelName : "MainMenu";
                    
                    Debug.Log($"[SERVER] ===== ALL PLAYERS VOTED =====");
                    Debug.Log($"[SERVER] Decision: {(allWantToContinue ? "Continue" : "Return to menu")}");
                    Debug.Log($"[SERVER] Loading scene: {targetScene}");
                    
                    LoadSceneMessage sceneMsg = new LoadSceneMessage(targetScene);
                    byte[] sceneData = NetworkSerializer.Serialize(sceneMsg);
                    if (sceneData != null)
                    {
                        BroadcastMessage(sceneData);
                        Debug.Log("[SERVER] LoadScene message broadcasted to all clients");
                    }

                    // Also process locally for the host
                    lock (sceneLoadLock)
                    {
                        pendingSceneToLoad = targetScene;
                        hasPendingSceneToLoad = true;
                        Debug.Log($"[SERVER] Set pending scene load: {targetScene}");
                    }

                    levelTransitionVotes.Clear();
                }
            }
        }
    }

    private void ProcessServerLevelCompleteMessage(byte[] buffer, int length)
    {
        SimpleMessage completeMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (completeMsg != null)
        {
            lock (votesLock)
            {
                nextLevelName = completeMsg.content;
                levelTransitionVotes.Clear();
                Debug.Log($"[SERVER] Level complete received. Next level set to: {nextLevelName}");
            }
            
            Debug.Log($"[SERVER] Broadcasting LEVEL_COMPLETE to all clients");
            BroadcastMessage(completeMsg);
            
            lock (levelCompleteLock)
            {
                shouldShowLevelTransitionUI = true;
            }
        }
    }

    public void SendPacket(byte[] outputPacket, IPEndPoint toAddress)
    {
        if (socket == null || outputPacket == null) return;

        try
        {
            socket.SendTo(outputPacket, toAddress);
        }
        catch (Exception e)
        {
            ReportError($"Error sending packet: {e.Message}");
        }
    }

    public void SendBytes(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            Debug.LogError("[NETWORK] SendBytes called with null or empty data!");
            return;
        }

        string msgType = NetworkSerializer.GetMessageType(data, data.Length);
        
        if (mode == NetworkMode.Client && serverEndPoint != null)
        {
            Debug.Log($"[CLIENT] Sending {msgType} message to server ({serverEndPoint})");
            SendPacket(data, serverEndPoint);
        }
        else if (mode == NetworkMode.Server)
        {
            Debug.Log($"[SERVER] Broadcasting {msgType} message to {connectedClients.Count} clients");
            BroadcastMessage(data);
        }
        else
        {
            Debug.LogWarning($"[NETWORK] SendBytes called but cannot send (mode: {mode}, serverEndPoint: {serverEndPoint})");
        }
    }

    private void BroadcastMessage(byte[] data)
    {
        if (mode != NetworkMode.Server) return;
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (ClientProxy client in connectedClients)
            {
                try
                {
                    SendPacket(data, client.endpoint);
                }
                catch (Exception e)
                {
                    ReportError($"Error broadcasting: {e.Message}");
                }
            }
        }
    }

    public void SendChatMessage(string message)
    {
        if (socket == null) return;

        ChatMessage chatMsg = new ChatMessage(username, message);
        byte[] data = NetworkSerializer.Serialize(chatMsg);

        if (data != null)
        {
            if (mode == NetworkMode.Client)
            {
                SendPacket(data, serverEndPoint);
            }
            else if (mode == NetworkMode.Server)
            {
                lock (chatLock)
                {
                    pendingChatMessage = chatMsg;
                    hasPendingChatMessage = true;
                }
                BroadcastMessage(chatMsg, null);
            }
        }
    }

    private void BroadcastMessage<T>(T message, ClientProxy excludeClient = null) where T : NetworkMessage
    {
        if (mode != NetworkMode.Server) return;

        byte[] data = NetworkSerializer.Serialize(message);
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (ClientProxy client in connectedClients)
            {
                if (excludeClient != null && client.endpoint.Equals(excludeClient.endpoint))
                    continue;

                try
                {
                    SendPacket(data, client.endpoint);
                }
                catch (Exception e)
                {
                    ReportError($"Error broadcasting: {e.Message}");
                }
            }
        }
    }

    // Connection management
    private void SendHandshake()
    {
        if (socket == null || serverEndPoint == null)
        {
            ReportError("Cannot send handshake: socket or server endpoint not initialized");
            return;
        }

        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(msg);

            if (data != null)
            {
                SendPacket(data, serverEndPoint);
                Debug.Log($"[CLIENT] Handshake sent to {serverIP}:{serverPort} with username: {username}");
            }
        }
        catch (Exception e)
        {
            ReportError($"Error sending handshake: {e.Message}");
        }
    }

    void OnConnectionReset(IPEndPoint fromAddress)
    {
        Debug.Log($"[NETWORK] Connection reset from {fromAddress}");

        if (mode == NetworkMode.Server)
        {
            lock (clientsLock)
            {
                ClientProxy clientToRemove = null;
                foreach (ClientProxy client in connectedClients)
                {
                    if (client.endpoint.Equals(fromAddress))
                    {
                        clientToRemove = client;
                        break;
                    }
                }

                if (clientToRemove != null)
                {
                    connectedClients.Remove(clientToRemove);
                    Debug.Log($"[SERVER] Removed client: {clientToRemove.username}");
                }
            }
        }
    }

    void OnDisconnect()
    {
        Shutdown();
    }

    void ReportError(string errorMessage)
    {
        Debug.LogError($"[NETWORK] {errorMessage}");
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

        Debug.Log($"[NETWORK] Shutting down {mode} networking...");

        try
        {
            if (socket != null)
            {
                socket.Close();
                socket = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NETWORK] Error during shutdown: {e.Message}");
        }

        Debug.Log("[NETWORK] Shutdown complete");
    }

    void OnApplicationQuit() => Shutdown();
    void OnDestroy() => Shutdown();
}