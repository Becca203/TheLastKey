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

    [Header("Reliability Settings")]
    [SerializeField] private bool enableReliability = true;
    private ReliabilityManager reliabilityManager;

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

    private bool connectionFailed = false;

    private Dictionary<int, long> lastPushTimestamps = new Dictionary<int, long>();
    private object pushTimestampLock = new object();

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

        // Reset connection state
        connectionFailed = false;
        waitingForServerResponse = false;
        connectionTimer = 0f;
    }

    void Start()
    {
        if (hasStarted)
        {
            Debug.LogWarning("[NETWORK] Start called multiple times - ignoring");
            return;
        }

        if (mode == NetworkMode.None)
        {
            Debug.LogError("[NETWORK] Mode not set! Use Initialize() before Start()");
            return;
        }

        if (replicationManager != null)
        {
            replicationManager.Initialize(mode == NetworkMode.Server);
        }
        else
        {
            Debug.LogWarning("[NETWORK] ReplicationManager not found");
        }

        if (enableReliability)
        {
            reliabilityManager = new ReliabilityManager();
            Debug.Log("[NETWORK] Reliability Manager initialized");
        }

        if (socket == null)
        {
            CreateAndBindSocket();
        }

        if (mode == NetworkMode.Client)
        {
            SimpleMessage usernameMsg = new SimpleMessage("USERNAME", username);
            byte[] data = NetworkSerializer.Serialize(usernameMsg);
            if (data != null)
            {
                SendBytes(data); 
                waitingForServerResponse = true;
                connectionTimer = 0f;
                Debug.Log($"[CLIENT] Sent USERNAME: {username} to {serverIP}");
            }
        }
        else if (mode == NetworkMode.Server)
        {
            Debug.Log($"[SERVER] Listening on port {serverPort}");
        }

        hasStarted = true;
        lastPingTime = Time.time;
        lastPingReceived = DateTime.Now;

        isInitialized = true;
    }

    private void HandleClientUpdate()
    {
        if (waitingForServerResponse)
        {
            connectionTimer += Time.deltaTime;
            if (connectionTimer > connectionTimeout)
            {
                Debug.LogError("[CLIENT] Connection timeout - server not responding");
                connectionFailed = true;
                waitingForServerResponse = false;

                // Clean up socket for retry
                CleanupSocket();
            }
        }

        double timeSinceLastPing = (DateTime.Now - lastPingReceived).TotalSeconds;
        if (timeSinceLastPing > inactivityDisconnectThreshold && !hasTriggeredDisconnection)
        {
            hasTriggeredDisconnection = true;
            ReturnToMainMenu();
        }
    }

    private void CleanupSocket()
    {
        isRunning = false;

        try
        {
            if (socket != null)
            {
                socket.Close();
                socket.Dispose();
                socket = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NETWORK] Error cleaning up socket: {e.Message}");
        }

        isInitialized = false;
        hasStarted = false;
    }


    // Retries the connection to the server. Call this after a failed connection attempt.
    public bool RetryConnection()
    {
        if (!connectionFailed && isInitialized)
        {
            Debug.LogWarning("[NETWORK] No need to retry - connection is fine");
            return false;
        }

        Debug.Log("[NETWORK] Retrying connection...");

        // Clean up previous attempt
        CleanupSocket();

        // Reset state
        connectionFailed = false;
        hasShutdown = false;
        isRunning = true;
        hasTriggeredDisconnection = false;

        // Restart
        Start();

        return isInitialized && !connectionFailed;
    }

    public bool IsConnectionFailed()
    {
        return connectionFailed;
    }

    private void ProcessUsernameMessage(byte[] buffer, int length)
    {
        SimpleMessage usernameMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (usernameMsg != null && usernameMsg.content.StartsWith("SERVER_NAME:"))
        {
            waitingForServerResponse = false;
            connectionFailed = false; // Connection successful
            shouldLoadWaitingRoom = true;
            Debug.Log("[CLIENT] Successfully connected to server");
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
                socket.Dispose();
                socket = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NETWORK] Error during shutdown: {e.Message}");
        }

        isInitialized = false;
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

        // Process reliable packet retransmissions
        if (enableReliability && reliabilityManager != null)
        {
            List<ReliablePacket> packetsToRetransmit = reliabilityManager.GetPacketsToRetransmit();
            foreach (ReliablePacket packet in packetsToRetransmit)
            {
                byte[] data = NetworkSerializer.SerializeReliable(packet);
                if (data != null)
                {
                    if (mode == NetworkMode.Client && serverEndPoint != null)
                    {
                        SendPacket(data, serverEndPoint);
                    }
                    else if (mode == NetworkMode.Server)
                    {
                        BroadcastToClients(data, null);
                    }
                }
            }
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
            SendBytes(data);
            lastPingTime = Time.time;
        }
        else if (mode == NetworkMode.Server)
        {
            lock (clientsLock)
            {
                foreach (ClientProxy client in connectedClients)
                {
                    SendBytes(data, client.endpoint); 
                }
            }
            lastPingTime = Time.time;
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
                    
                    if (player != null)
                    {
                        if (player.isPushed)
                        {
                            Debug.Log($"[Networking] DISCARDING queued PUSH - Player {pendingPush.playerID} already pushed");
                            hasPendingPush = false;
                        }
                        else
                        {
                            Debug.Log($"[Networking] Applying PUSH to {(player.isLocalPlayer ? "LOCAL" : "REMOTE")} Player {pendingPush.playerID}: vel={pendingPush.velocity}, dur={pendingPush.duration}");
                            player.StartPush(pendingPush.velocity, pendingPush.duration);
                            hasPendingPush = false;
                        }
                    }
                    else
                    {
                        Debug.LogError($"[Networking] Player {pendingPush.playerID} not found for push");
                        hasPendingPush = false;
                    }
                }
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
        string msgType = "UNKNOWN";
        byte[] messageData = buffer;
        int messageLength = length;

        try
        {
            msgType = NetworkSerializer.GetMessageType(buffer, length);
        }
        catch
        {
            msgType = "UNKNOWN";
        }

        if (msgType == "UNKNOWN" || buffer.Length > 1000) 
        {
            try
            {
                ReliablePacket reliablePacket = NetworkSerializer.DeserializeReliable(buffer, length);
                if (reliablePacket != null && reliablePacket.payload != null && reliablePacket.payload.Length > 0)
                {
                    msgType = reliablePacket.messageType;
                    messageData = reliablePacket.payload;
                    messageLength = reliablePacket.payload.Length;

                    if (reliablePacket.needsAck)
                    {
                        SendAck(reliablePacket.sequenceNumber, fromAddress);
                    }

                    Debug.Log($"[RELIABLE] Received: {msgType} (seq: {reliablePacket.sequenceNumber})");
                }
            }
            catch {}
        }

        if (mode == NetworkMode.Server)
        {
            ClientProxy client = GetOrCreateClient(fromAddress);
            ProcessServerMessage(msgType, messageData, messageLength, client);
        }
        else if (mode == NetworkMode.Client)
        {
            ProcessClientMessage(msgType, messageData, messageLength);
        }
    }

    // Process reliable packets and handle ACKs
    private void HandleReliablePacket(ReliablePacket packet, IPEndPoint fromAddress)
    {
        // If ACK packet, process acknowledgment
        if (packet.isAck)
        {
            reliabilityManager.AcknowledgePacket(packet.ackSequence);
            Debug.Log($"[RELIABLE] Received ACK for sequence {packet.ackSequence}");
            return;
        }

        // Update last received sequence and send ACK if needed
        uint lastSeq = reliabilityManager.GetLastReceivedSequence();
        if (packet.sequenceNumber > lastSeq)
        {
            reliabilityManager.UpdateLastReceivedSequence(packet.sequenceNumber);
        }
        else if (packet.sequenceNumber == lastSeq)
        {
            Debug.LogWarning($"[RELIABLE] Duplicate packet {packet.sequenceNumber}, ignoring");
            SendAck(packet.sequenceNumber, fromAddress);
            return;
        }

        if (packet.needsAck)
            SendAck(packet.sequenceNumber, fromAddress);

        if (packet.payload != null && packet.payload.Length > 0)
        {
            string msgType = packet.messageType;
            if (mode == NetworkMode.Server)
            {
                ClientProxy client = GetOrCreateClient(fromAddress);
                ProcessServerMessage(msgType, packet.payload, packet.payload.Length, client);
            }
            else if (mode == NetworkMode.Client)
            {
                ProcessClientMessage(msgType, packet.payload, packet.payload.Length);
            }
        }
    }

    private void SendAck(uint sequenceNumber, IPEndPoint toAddress)
    {
        ReliablePacket ackPacket = new ReliablePacket
        {
            sequenceNumber = reliabilityManager.GetNextSequence(),
            ackSequence = sequenceNumber,
            isAck = true,
            needsAck = false,
            messageType = "ACK",
            payload = null
        };

        byte[] ackData = NetworkSerializer.SerializeReliable(ackPacket);
        if (ackData != null)
        {
            SendPacket(ackData, toAddress);
            Debug.Log($"[RELIABLE] Sent ACK for sequence {sequenceNumber}");
        }
    }

    private void ProcessClientMessage(string msgType, byte[] buffer, int length)
    {
        if (msgType == "ACK") return;

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
                Debug.LogWarning("[CLIENT] Received LEVEL_TRANSITION");
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
                Debug.LogWarning($"[CLIENT] Unknown message type: {msgType}");
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
        GameStartMessage startMsg = NetworkSerializer.Deserialize<GameStartMessage>(buffer, length);
        if (startMsg != null)
        {
            lock (mainThreadActionsLock)
            {
                shouldSetPlayerID = true;
                assignedPlayerID = startMsg.assignedPlayerID; 
                
                mainThreadActions.Enqueue(() => {
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.SetLocalPlayerID(assignedPlayerID);
                        Debug.Log($"[Networking] Game started! Your Player ID: {assignedPlayerID}");
                    }
                });
            }

            lock (sceneLoadLock)
            {
                shouldLoadGameScene = true;
                pendingSceneToLoad = "GameScene";
            }

            Debug.Log($"[Networking] GAME_START received - assigned Player ID: {startMsg.assignedPlayerID}");
        }
    }

    private void ProcessPositionMessage(byte[] buffer, int length)
    {
        PositionMessage posMsg = NetworkSerializer.Deserialize<PositionMessage>(buffer, length);
        if (posMsg != null)
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                NetworkPlayer player = gameManager.FindPlayerByID(posMsg.playerID);
                if (player != null && player.isPushed)
                {
                    Debug.Log($"[Networking] DISCARDING position update for pushed Player {posMsg.playerID}");
                    return; 
                }
            }

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
        if (pushMsg == null) return;

        Debug.Log($"[Networking] Received PUSH for pushedPlayerID = {pushMsg.pushedPlayerID}");

        lock (pushTimestampLock)
        {
            if (lastPushTimestamps.ContainsKey(pushMsg.pushedPlayerID))
            {
                long lastTimestamp = lastPushTimestamps[pushMsg.pushedPlayerID];
                if (pushMsg.timestamp <= lastTimestamp)
                {
                    Debug.Log($"[Networking] IGNORING PUSH - Duplicate/old message (timestamp: {pushMsg.timestamp} <= {lastTimestamp})");
                    return;
                }
            }
            lastPushTimestamps[pushMsg.pushedPlayerID] = pushMsg.timestamp;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            NetworkPlayer player = gameManager.FindPlayerByID(pushMsg.pushedPlayerID);
            if (player != null && player.isPushed)
            {
                Debug.Log($"[Networking] IGNORING PUSH - Player {pushMsg.pushedPlayerID} is already pushed");
                
                lock (pushLock)
                {
                    if (hasPendingPush && pendingPush.playerID == pushMsg.pushedPlayerID)
                    {
                        hasPendingPush = false;
                    }
                }
                return;
            }
        }

        lock (pushLock)
        {
            if (hasPendingPush && pendingPush.playerID == pushMsg.pushedPlayerID)
            {
                Debug.Log($"[Networking] IGNORING PUSH - Already have pending push for Player {pushMsg.pushedPlayerID}");
                return;
            }

            pendingPush = new PushData
            {
                playerID = pushMsg.pushedPlayerID,
                velocity = new Vector2(pushMsg.velocityX, pushMsg.velocityY),
                duration = pushMsg.duration
            };
            hasPendingPush = true;
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
 
        switch (msgType)
        {
            case "PING":
                client.lastPingTime = DateTime.Now;
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
        SimpleMessage serverNameMsg = new SimpleMessage("USERNAME", "SERVER_NAME:" + serverName);
        byte[] data = NetworkSerializer.Serialize(serverNameMsg);
        if (data != null)
        {
            SendBytes(data, clientEndpoint);
            Debug.Log($"[SERVER] Sent server name to {clientEndpoint}");
        }
    }

    private void SendUserList()
    {
        lock (clientsLock)
        {
            PlayerListMessage listMsg = new PlayerListMessage();
            listMsg.players.Clear();

            foreach (ClientProxy client in connectedClients)
            {
                if (!string.IsNullOrEmpty(client.username))
                {
                    listMsg.players.Add(client.username);
                }
            }

            byte[] data = NetworkSerializer.Serialize(listMsg);
            if (data != null)
            {
                BroadcastToClients(data, null);
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
            int connectedCount = connectedClients.Count;

            if (connectedCount < 2)
            {
                Debug.LogWarning($"[SERVER] Cannot start game: only {connectedCount}/2 players connected");
                return;
            }

            if (gameStarted)
            {
                Debug.LogWarning("[SERVER] Game already started");
                return;
            }

            gameStarted = true;

            // Reset key state
            lock (keyStateLock)
            {
                keyCollected = false;
                keyOwnerPlayerID = -1;
            }

            Debug.Log($"[SERVER] Starting game with {connectedCount} players");

            // Send GAME_START to each client with their assigned player ID
            int playerIDCounter = 1;
            foreach (ClientProxy client in connectedClients)
            {
                client.playerID = playerIDCounter;
                
                GameStartMessage startMsg = new GameStartMessage(playerIDCounter, connectedCount);
                byte[] startData = NetworkSerializer.Serialize(startMsg);
                
                if (startData != null)
                {
                    SendPacket(startData, client.endpoint);
                    Debug.Log($"[SERVER] Sent GAME_START to {client.username} (PlayerID: {playerIDCounter})");
                }
                
                playerIDCounter++;
            }

            // Send LOAD_SCENE to all clients
            LoadSceneMessage sceneMsg = new LoadSceneMessage("GameScene");
            byte[] sceneData = NetworkSerializer.Serialize(sceneMsg);
            
            if (sceneData != null)
            {
                BroadcastToClients(sceneData, null);
                Debug.Log("[SERVER] Broadcast LOAD_SCENE to all clients");
            }
        }
    }

    private void ProcessServerKeyCollectedMessage(byte[] buffer, int length, ClientProxy client)
    {
        if (client == null) return;

        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null && keyMsg.messageType == "KEY_COLLECTED" && int.TryParse(keyMsg.content, out int playerID))
        {
            lock (keyStateLock)
            {
                if (keyCollected)
                {
                    SendKeyRejection(client.endpoint, keyOwnerPlayerID);
                    return;
                }
                keyCollected = true;
                keyOwnerPlayerID = playerID;
            }

            byte[] keyData = NetworkSerializer.Serialize(keyMsg);
            if (keyData != null)
            {
                BroadcastToClients(keyData, null);
            }

            SimpleMessage hideMsg = new SimpleMessage("HIDE_KEY", "");
            byte[] hideData = NetworkSerializer.Serialize(hideMsg);
            if (hideData != null)
            {
                BroadcastToClients(hideData, null);
            }

            Debug.Log($"[SERVER] Key collected by player {playerID} (broadcast KEY_COLLECTED + HIDE_KEY)");
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
        if (transferMsg == null) return;

        bool validTransfer = false;
        int currentOwner;

        lock (keyStateLock)
        {
            currentOwner = keyOwnerPlayerID;

            if (!keyCollected)
            {
                Debug.LogWarning("[SERVER] Ignoring KEY_TRANSFER: key not collected yet");
            }
            else if (keyOwnerPlayerID != transferMsg.fromPlayerID)
            {
                Debug.LogWarning($"[SERVER] Ignoring KEY_TRANSFER: server owner={keyOwnerPlayerID}, msg.from={transferMsg.fromPlayerID}");
            }
            else
            {
                keyOwnerPlayerID = transferMsg.toPlayerID;
                validTransfer = true;
            }
        }

        if (validTransfer)
        {
            byte[] data = NetworkSerializer.Serialize(transferMsg);
            if (data != null)
            {
                BroadcastToClients(data, null);
                Debug.Log($"[SERVER] KEY_TRANSFER broadcast: {transferMsg.fromPlayerID} -> {transferMsg.toPlayerID}");
            }
        }
        else
        {
            SimpleMessage corr = new SimpleMessage("KEY_COLLECTED", currentOwner.ToString());
            byte[] corrData = NetworkSerializer.Serialize(corr);
            if (corrData != null)
            {
                BroadcastToClients(corrData, null);
                Debug.Log($"[SERVER] Resync owner via KEY_COLLECTED: owner={currentOwner}");
            }
        }
    }

    private void ProcessServerPushMessage(byte[] buffer, int length)
    {
        PushMessage pushMsg = NetworkSerializer.Deserialize<PushMessage>(buffer, length);
        if (pushMsg == null) return;

        Debug.Log($"[SERVER] Received PUSH for Player {pushMsg.pushedPlayerID}");

        byte[] data = NetworkSerializer.Serialize(pushMsg);
        if (data != null)
        {
            BroadcastToClients(data, null);
            Debug.Log($"[SERVER] Broadcasted PUSH to all clients for Player {pushMsg.pushedPlayerID}");
        }

        if (replicationManager != null)
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

    private bool MessageNeedsReliability(string messageType)
    {
        switch (messageType)
        {
            case "KEY_COLLECTED":
            case "KEY_TRANSFER":
            case "PUSH": 
            case "TRAP_PLACED":
            case "TRAP_TRIGGERED":
            case "START_GAME":
            case "GAME_START":
            case "LEVEL_COMPLETE":
            case "LOAD_SCENE":
                return true;
            
            default:
                return false;
        }
    }

    // Send secure packets with ACKs for critical messages
    public void SendBytesReliable(byte[] data, string messageType = "")
    {
        if (!enableReliability || reliabilityManager == null)
        {
            SendBytes(data);
            return;
        }

        if (!string.IsNullOrEmpty(messageType) && !MessageNeedsReliability(messageType))
        {
            SendBytes(data);
            return;
        }

        ReliablePacket packet = new ReliablePacket
        {
            sequenceNumber = reliabilityManager.GetNextSequence(),
            ackSequence = reliabilityManager.GetLastReceivedSequence(),
            isAck = false,
            needsAck = true,
            messageType = messageType,
            payload = data,
            timestamp = System.DateTime.Now.Ticks
        };

        byte[] packetData = NetworkSerializer.SerializeReliable(packet);
        if (packetData != null)
        {
            SendBytes(packetData);
            reliabilityManager.RegisterPendingPacket(packet);
            Debug.Log($"[RELIABLE] Sent packet #{packet.sequenceNumber} ({messageType})");
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

    public void SendBytes(byte[] data, IPEndPoint toAddress)
    {
        if (data == null || data.Length == 0) return;
        if (toAddress == null) return;

        SendPacket(data, toAddress);
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

    
}