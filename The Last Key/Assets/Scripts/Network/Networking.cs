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

    [Header("Replication")]
    private ReplicationManager replicationManager;

    [Header("Disconnection Detection")]
    [SerializeField] private float inactivityDisconnectThreshold = 60f;  // For long inactivity
    private bool hasTriggeredDisconnection = false;

    private Socket socket;
    private IPEndPoint serverEndPoint;
    private bool isRunning = true;
    private bool hasShutdown = false;
    private bool isInitialized = false;

    private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool waitingForServerResponse = false;
    private float lastPingTime = 0f;
    private DateTime lastPingReceived = DateTime.Now;

    private string serverName = "GameServer";
    private List<ClientProxy> connectedClients = new List<ClientProxy>();
    private object clientsLock = new object();
    private bool gameStarted = false;

    private bool keyCollected = false;
    private int keyOwnerPlayerID = -1;
    private object keyStateLock = new object();

    private Dictionary<int, bool> levelTransitionVotes = new Dictionary<int, bool>();
    private object votesLock = new object();
    private string nextLevelName = "";

    private WaitingRoom waitingRoom;
    private bool shouldLoadWaitingRoom = false;
    private bool shouldLoadGameScene = false;
    private int assignedPlayerID = 0;
    private bool shouldSetPlayerID = false;

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

    private TrapPlacementData pendingTrapPlacement;
    private bool hasPendingTrapPlacement = false;
    private object trapPlacementLock = new object();
    private int pendingTrapTriggeredPlayerID = -1;
    private bool hasPendingTrapTriggered = false;
    private object trapTriggeredLock = new object();
    private Vector3 pendingTrapPosition = Vector3.zero;

    private string pendingSceneToLoad = null;
    private bool hasPendingSceneToLoad = false;
    private object sceneLoadLock = new object();

    private bool shouldShowLevelTransitionUI = false;
    private object levelCompleteLock = new object();
    private Queue<Action> mainThreadActions = new Queue<Action>();
    private object mainThreadActionsLock = new object();

    private bool hasStarted = false;

    private bool shouldSwitchToMainCamera = false;
    private object cameraSwitchLock = new object();

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

    private struct TrapPlacementData
    {
        public int playerID;
        public Vector3 position;
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

        replicationManager = gameObject.AddComponent<ReplicationManager>();
        if (replicationManager != null)
        {
            replicationManager.Initialize(mode == NetworkMode.Server);
        }
        else
        {
            Debug.LogError("[NETWORK] Failed to create ReplicationManager!");
        }

        CreateAndBindSocket();

        if (socket == null)
        {
            Debug.LogError("[NETWORK] Failed to create socket!");
            return;
        }

        if (mode == NetworkMode.Client)
        {
            SendHandshake();
            connectionTimer = 0f;
            waitingForServerResponse = true;
        }
        else if (mode == NetworkMode.Server)
        {
            Debug.Log($"[SERVER] Listening on port {serverPort}");
        }

        isInitialized = true;
        lastPingTime = Time.time;
        lastPingReceived = DateTime.Now;
    }

    private void CreateAndBindSocket()
    {
        try
        {
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
                    socket.Blocking = true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SERVER] Failed to bind socket: {e.Message}");
                    try 
                    { 
                        socket.Close(); 
                        socket.Dispose(); 
                    } 
                    catch { }
                    socket = null;
                    Debug.LogError($"Failed to bind server socket: {e.Message}");
                    return;
                }
            }
            else if (mode == NetworkMode.Client)
            {
                try
                {
                    IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    socket.Bind(clientEndPoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CLIENT] Failed to bind socket: {e.Message}");
                    Debug.LogError($"Failed to bind client socket: {e.Message}");
                    socket?.Close();
                    socket = null;
                    return;
                }

                socket.Blocking = true;
                
                try
                {
                    IPAddress serverAddr = IPAddress.Parse(serverIP);
                    serverEndPoint = new IPEndPoint(serverAddr, serverPort);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CLIENT] Failed to parse server IP: {e.Message}");
                    socket?.Close();
                    socket = null;
                    return;
                }
            }

            Thread receiveThread = new Thread(ReceiveMessages)
            {
                IsBackground = true,
                Name = $"UDP_Receive_Thread_{mode}"
            };
            receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NETWORK] Socket creation failed: {e.Message}");
            Debug.LogError($"Socket creation failed: {e.Message}");
            socket = null;
        }
    }

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

        bool switchCamera = false;
        lock (cameraSwitchLock)
        {
            if (shouldSwitchToMainCamera)
            {
                switchCamera = true;
                shouldSwitchToMainCamera = false;
            }
        }

        if (switchCamera)
        {
            CameraSequenceManager sequenceManager = FindAnyObjectByType<CameraSequenceManager>();
            if (sequenceManager != null)
            {
                sequenceManager.SwitchToMainCamera();
                Debug.Log("[Networking] Switched to Main Camera via network message");
            }

            NetworkPlayer[] allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            foreach (NetworkPlayer player in allPlayers)
            {
                PlayerCameraController cameraController = player.GetComponent<PlayerCameraController>();
                if (cameraController != null)
                {
                    cameraController.ForceMainCamera();
                }
            }
        }
    }

    private void HandleClientUpdate()
    {
        if (waitingForServerResponse)
        {
            connectionTimer += Time.deltaTime;
            if (connectionTimer > connectionTimeout)
            {
                waitingForServerResponse = false;
            }
        }

        double timeSinceLastPing = (DateTime.Now - lastPingReceived).TotalSeconds;
        if (timeSinceLastPing > inactivityDisconnectThreshold && !hasTriggeredDisconnection)
        {
            hasTriggeredDisconnection = true;
            ReturnToMainMenu();
        }
    }

    private void HandleServerUpdate()
    {
        lock (clientsLock)
        {
            DateTime now = DateTime.Now;
            List<ClientProxy> clientsToRemove = new List<ClientProxy>();

            foreach (ClientProxy client in connectedClients)
            {
                double timeSinceLastPing = (now - client.lastPingTime).TotalSeconds;
                
                if (timeSinceLastPing > inactivityDisconnectThreshold)
                {
                    clientsToRemove.Add(client);
                }
            }

            foreach (ClientProxy client in clientsToRemove)
            {
                OnClientDisconnected(client);
            }
        }
    }

    private void OnClientDisconnected(ClientProxy client)
    {
        lock (clientsLock)
        {
            connectedClients.Remove(client);
        }

        SimpleMessage disconnectMsg = new SimpleMessage("PLAYER_LEFT", client.username);
        byte[] disconnectData = NetworkSerializer.Serialize(disconnectMsg);
        if (disconnectData != null)
        {
            BroadcastToClients(disconnectData, null);
        }

        lock (clientsLock)
        {
            if (connectedClients.Count <= 1)
            {
                lock (mainThreadActionsLock)
                {
                    mainThreadActions.Enqueue(() =>
                    {
                        ReturnToMainMenu();
                    });
                }
            }
        }
    }

    private void BroadcastToClients(byte[] data, IPEndPoint excludeEndpoint)
    {
        lock (clientsLock)
        {
            foreach (ClientProxy client in connectedClients)
            {
                if (excludeEndpoint == null || !client.endpoint.Equals(excludeEndpoint))
                {
                    try
                    {
                        socket.SendTo(data, client.endpoint);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[SERVER] Error sending to {client.username}: {e.Message}");
                    }
                }
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
        lock (mainThreadActionsLock)
        {
            while (mainThreadActions.Count > 0)
            {
                Action action = mainThreadActions.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NETWORK] Error executing main thread action: {e.Message}");
                }
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

        if (shouldHideKey)
        {
            lock (hideKeyLock)
            {
                KeyBehaviour key = FindAnyObjectByType<KeyBehaviour>();
                if (key != null && key.gameObject.activeSelf)
                {
                    key.gameObject.SetActive(false);
                }
                shouldHideKey = false;
            }
        }

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

        if (hasPendingPush)
        {
            lock (pushLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    NetworkPlayer player = gameManager.FindPlayerByID(pendingPush.playerID);
                    
                    // ✅ SOLO aplicar si este jugador es LOCAL en ESTA pantalla
                    if (player != null && player.isLocalPlayer)
                    {
                        Debug.Log($"[Networking] Applying PUSH to LOCAL Player {player.playerID}: vel={pendingPush.velocity}, dur={pendingPush.duration}");
                        player.StartPush(pendingPush.velocity, pendingPush.duration);
                    }
                    else if (player != null && !player.isLocalPlayer)
                    {
                        Debug.Log($"[Networking] Skipping PUSH for REMOTE Player {player.playerID} (already applied by pusher)");
                    }
                    else
                    {
                        Debug.LogError($"[Networking] Player {pendingPush.playerID} not found for PUSH");
                    }
                }
                hasPendingPush = false;
            }
        }

        if (shouldShowLevelTransitionUI)
        {
            lock (levelCompleteLock)
            {
                LevelTransitionUI transitionUI = FindAnyObjectByType<LevelTransitionUI>();
                if (transitionUI != null)
                {
                    transitionUI.ShowPanel();
                }
                shouldShowLevelTransitionUI = false;
            }
        }

        if (hasPendingSceneToLoad)
        {
            lock (sceneLoadLock)
            {
                if (!string.IsNullOrEmpty(pendingSceneToLoad))
                {
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

        if (hasPendingTrapPlacement)
        {
            lock (trapPlacementLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    gameManager.SpawnTrap(pendingTrapPlacement.playerID, pendingTrapPlacement.position);
                }
                hasPendingTrapPlacement = false;
            }
        }

        if (hasPendingTrapTriggered)
        {
            lock (trapTriggeredLock)
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager != null)
                {
                    gameManager.DestroyTrapAt(pendingTrapPosition);
                    NetworkPlayer player = gameManager.FindPlayerByID(pendingTrapTriggeredPlayerID);
                    if (player != null) 
                        gameManager.RespawnPlayer(player); 
                }
                hasPendingTrapTriggered = false;
                pendingTrapTriggeredPlayerID = -1;
            }
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
                break;
            case "TRAP_PLACED":
                ProcessTrapPlacedMessage(buffer, length);
                break;
            case "TRAP_TRIGGERED":
                ProcessTrapTriggeredMessage(buffer, length);
                break;
            case "CAMERA_SWITCH":
                ProcessCameraSwitchMessage(buffer, length);
                break;
            default:
                Debug.LogWarning("[CLIENT] Unknown message type: " + msgType);
                break;
        }
    }

    private void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ResetNetwork();
        }
        
        if (GameManager.Instance != null)
        {
            Destroy(GameManager.Instance.gameObject);
        }
        
        SceneManager.LoadScene("MainMenu");
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
            
            if (!hasTriggeredDisconnection)
            {
                lock (mainThreadActionsLock)
                {
                    mainThreadActions.Enqueue(() =>
                    {
                        if (SceneManager.GetActiveScene().name != "WaitingRoom" && !hasTriggeredDisconnection)
                        {
                            hasTriggeredDisconnection = true;
                            ReturnToMainMenu();
                        }
                    });
                }
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
            Debug.Log($"[Networking] Received PUSH for pushedPlayerID = {pushMsg.pushedPlayerID}"); 
            
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
        if (sceneMsg != null && !string.IsNullOrEmpty(sceneMsg.sceneName))
        {
            // Queue the player state reset to be executed on the main thread
            lock (mainThreadActionsLock)
            {
                mainThreadActions.Enqueue(() =>
                {
                    // Reset player states before scene change
                    GameManager gameManager = GameManager.Instance;
                    if (gameManager != null)
                    {
                        NetworkPlayer[] allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
                        foreach (NetworkPlayer player in allPlayers)
                        {
                            player.SetHasKey(false);
                        }
                    }
                });
            }
            
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
            
            lock (levelCompleteLock)
            {
                shouldShowLevelTransitionUI = true;
            }
        }
    }

    private void ProcessTrapPlacedMessage(byte[] buffer, int length)
    {
        TrapPlacedMessage trapMsg = NetworkSerializer.Deserialize<TrapPlacedMessage>(buffer, length);
        
        if (trapMsg != null)
        {
            lock (trapPlacementLock)
            {
                pendingTrapPlacement = new TrapPlacementData
                {
                    playerID = trapMsg.playerID,
                    position = trapMsg.GetPosition()
                };
                hasPendingTrapPlacement = true;
                
                Debug.Log($"[Networking] Client received TRAP_PLACED from Player {trapMsg.playerID}");
            }
        }
    }

    private void ProcessTrapTriggeredMessage(byte[] buffer, int length)
    {
        TrapTriggeredMessage triggerMsg = NetworkSerializer.Deserialize<TrapTriggeredMessage>(buffer, length);
        
        if (triggerMsg != null)
        {
            lock (trapTriggeredLock)
            {
                pendingTrapTriggeredPlayerID = triggerMsg.triggeredPlayerID;
                pendingTrapPosition = triggerMsg.GetPosition();
                hasPendingTrapTriggered = true;
                
                Debug.Log($"[Networking] Client received TRAP_TRIGGERED for Player {triggerMsg.triggeredPlayerID}");
            }
        }
    }

    private void ProcessCameraSwitchMessage(byte[] buffer, int length)
    {
        CameraSwitchMessage cameraMsg = NetworkSerializer.Deserialize<CameraSwitchMessage>(buffer, length);
        if (cameraMsg != null)
        {
            lock (cameraSwitchLock)
            {
                shouldSwitchToMainCamera = cameraMsg.switchToMainCamera;
            }
            Debug.Log($"[Networking] Received CAMERA_SWITCH to main camera: {cameraMsg.switchToMainCamera}");
        }
    }

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
            case "CLIENT_QUIT":
                OnClientDisconnected(client);
                break;
            case "TRAP_PLACED":
                ProcessServerTrapPlacedMessage(buffer, length, client);
                break;
            case "TRAP_TRIGGERED":
                ProcessServerTrapTriggeredMessage(buffer, length);
                break;
            case "CAMERA_SWITCH":
                ProcessServerCameraSwitchMessage(buffer, length);
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
                Debug.LogWarning($"[SERVER] Max clients (2) already connected");
                return null;
            }

            ClientProxy newClient = new ClientProxy(
                endpoint,
                "",
                connectedClients.Count + 1
            );
            connectedClients.Add(newClient);

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
            Debug.LogError($"Error sending server name: {e.Message}");
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
                    Debug.LogError($"Error sending user list: {e.Message}");
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

        lock (clientsLock)
        {
            sender.lastPingTime = DateTime.Now;
        }

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

            if (uniqueClients >= 2)
            {
                if (gameStarted)
                {
                    Debug.LogWarning("[SERVER] Game already started");
                    return;
                }

                gameStarted = true;

                lock (keyStateLock)
                {
                    keyCollected = false;
                    keyOwnerPlayerID = -1;
                }

                int playerID = 1;
                foreach (ClientProxy client in connectedClients)
                {
                    client.playerID = playerID;
                    
                    GameStartMessage startMsg = new GameStartMessage(playerID, uniqueClients);
                    byte[] data = NetworkSerializer.Serialize(startMsg);
                    
                    if (data != null)
                    {
                        SendPacket(data, client.endpoint);
                    }
                    
                    playerID++;
                }
            }
        }
    }

    private void ProcessServerKeyCollectedMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null) return;

        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null && replicationManager != null)
        {
            if (int.TryParse(keyMsg.content, out int playerID))
            {
                lock (keyStateLock)
                {
                    if (keyCollected)
                    {
                        Debug.LogWarning($"[SERVER] REJECTED: Player {playerID} tried to collect key, but Player {keyOwnerPlayerID} has it");
                        SendKeyRejection(client.endpoint, keyOwnerPlayerID);
                        return;
                    }

                    keyCollected = true;
                    keyOwnerPlayerID = playerID;
                }
                replicationManager.ReplicateKeyCollection(playerID);
            }
        }
    }

    private void SendKeyRejection(IPEndPoint clientEndpoint, int actualOwnerID)
    {
        SimpleMessage hideKeyMsg = new SimpleMessage("HIDE_KEY", "");
        byte[] hideData = NetworkSerializer.Serialize(hideKeyMsg);
        if (hideData != null)
        {
            SendPacket(hideData, clientEndpoint);
        }

        SimpleMessage keyStateMsg = new SimpleMessage("KEY_COLLECTED", actualOwnerID.ToString());
        byte[] keyData = NetworkSerializer.Serialize(keyStateMsg);
        if (keyData != null)
        {
            SendPacket(keyData, clientEndpoint);
        }
    }

    private void ProcessServerKeyTransferMessage(byte[] buffer, int length)
    {
        KeyTransferMessage transferMsg = NetworkSerializer.Deserialize<KeyTransferMessage>(buffer, length);
        if (transferMsg != null && replicationManager != null)
        {
            lock (keyStateLock)
            {
                if (!keyCollected || keyOwnerPlayerID != transferMsg.fromPlayerID)
                {
                    Debug.LogWarning($"[SERVER] Key transfer rejected");
                    return;
                }

                keyOwnerPlayerID = transferMsg.toPlayerID;
            }

            replicationManager.ReplicateKeyTransfer(transferMsg.fromPlayerID, transferMsg.toPlayerID);
        }
    }

    private void ProcessServerPushMessage(byte[] buffer, int length)
    {
        PushMessage pushMsg = NetworkSerializer.Deserialize<PushMessage>(buffer, length);
        if (pushMsg != null && replicationManager != null)
        {
            replicationManager.ReplicatePush(
                pushMsg.pushedPlayerID,
                new Vector2(pushMsg.velocityX, pushMsg.velocityY),
                pushMsg.duration
            );
        }
    }

    private void ProcessLevelTransitionMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null) return;

        LevelTransitionMessage transitionMsg = NetworkSerializer.Deserialize<LevelTransitionMessage>(buffer, length);
        if (transitionMsg != null)
        {
            lock (votesLock)
            {
                levelTransitionVotes[transitionMsg.playerID] = transitionMsg.wantsToContinue;

                int totalPlayers = 2;

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
                    
                    LoadSceneMessage sceneMsg = new LoadSceneMessage(targetScene);
                    byte[] sceneData = NetworkSerializer.Serialize(sceneMsg);
                    if (sceneData != null)
                    {
                        BroadcastMessage(sceneData);
                    }

                    lock (sceneLoadLock)
                    {
                        pendingSceneToLoad = targetScene;
                        hasPendingSceneToLoad = true;
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
            }
            
            lock (keyStateLock)
            {
                keyCollected = false;
                keyOwnerPlayerID = -1;
            }
            
            BroadcastMessage(completeMsg);
            
            lock (levelCompleteLock)
            {
                shouldShowLevelTransitionUI = true;
            }
        }
    }

    private void ProcessServerCameraSwitchMessage(byte[] buffer, int length)
    {
        CameraSwitchMessage cameraMsg = NetworkSerializer.Deserialize<CameraSwitchMessage>(buffer, length);

        if (cameraMsg != null)
        {
            byte[] data = NetworkSerializer.Serialize(cameraMsg);
            if (data != null)
            {
                BroadcastToClients(data, null); // null = enviar a todos
            }
        }
    }

    private void ProcessServerTrapPlacedMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null) return;

        TrapPlacedMessage trapMsg = NetworkSerializer.Deserialize<TrapPlacedMessage>(buffer, length);
        
        if (trapMsg != null && replicationManager != null)
        {
            Debug.Log($"[SERVER] Player {trapMsg.playerID} placed trap at {trapMsg.GetPosition()}");
            
            byte[] data = NetworkSerializer.Serialize(trapMsg);
            if (data != null) 
                BroadcastToClients(data, null); 
        }
    }

    private void ProcessServerTrapTriggeredMessage(byte[] buffer, int length)
    {
        TrapTriggeredMessage triggerMsg = NetworkSerializer.Deserialize<TrapTriggeredMessage>(buffer, length);
        
        if (triggerMsg != null && replicationManager != null)
        {
            Debug.Log($"[SERVER] Player {triggerMsg.triggeredPlayerID} triggered trap");
            
            byte[] data = NetworkSerializer.Serialize(triggerMsg);
            if (data != null)
                BroadcastToClients(data, null);
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
            Debug.LogError($"Error sending packet: {e.Message}");
        }
    }

    public void SendBytes(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        if (mode == NetworkMode.Client && serverEndPoint != null)
        {
            SendPacket(data, serverEndPoint);
        }
        else if (mode == NetworkMode.Server)
        {
            BroadcastMessage(data);
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
                    Debug.LogError($"Error broadcasting: {e.Message}");
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
                    Debug.LogError($"Error broadcasting: {e.Message}");
                }
            }
        }
    }

    private void SendHandshake()
    {
        if (socket == null || serverEndPoint == null)
        {
            Debug.LogError("Cannot send handshake: socket or server endpoint not initialized");
            return;
        }

        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(msg);

            if (data != null)
            {
                SendPacket(data, serverEndPoint);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending handshake: {e.Message}");
        }
    }

    void OnConnectionReset(IPEndPoint fromAddress)
    {
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
                }
            }
        }
    }

    void OnDisconnect()
    {
        Shutdown();
    }

    private WaitingRoom GetWaitingRoomManager()
    {
        if (waitingRoom == null)
        {
            waitingRoom = FindAnyObjectByType<WaitingRoom>();
        }
        return waitingRoom;
    }

    void OnApplicationQuit() 
    {
        SendClientQuitNotification();
        Thread.Sleep(100); 
        Shutdown();
    }

    void OnDestroy() 
    {
        SendClientQuitNotification();
        Thread.Sleep(50);
        Shutdown();
    }

    void OnDisable()
    {
        SendClientQuitNotification();
    }

    private void SendClientQuitNotification()
    {
        if (hasShutdown) return; 
        
        if (mode == NetworkMode.Client && socket != null && serverEndPoint != null)
        {
            try
            {
                SimpleMessage quitMsg = new SimpleMessage("CLIENT_QUIT", username);
                byte[] data = NetworkSerializer.Serialize(quitMsg);
                if (data != null)
                {
                    SendPacket(data, serverEndPoint);
                    try
                    {
                        socket.Blocking = true;
                    }
                    catch { }
                    
                    Debug.Log("[CLIENT] Sent quit notification to server");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CLIENT] Could not send quit notification: {e.Message}");
            }
        }
    }

    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;

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
    }
}