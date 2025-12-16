using System;
using System.Collections.Generic;
using UnityEngine;

/// Central manager for World State Replication
/// Handles serialization, deserialization and dispatch of replication packets
/// Uses Passive Replication model (Server authoritative)
public class ReplicationManager : MonoBehaviour
{
    public static ReplicationManager Instance { get; private set; }

    [Header("Replication Configuration")]
    [SerializeField] private bool isServer = false;
    [SerializeField] private float replicationRate = 20f; // packets per second
    
    private Networking networking;
    private Dictionary<int, NetworkPlayer> registeredPlayers = new Dictionary<int, NetworkPlayer>();
    private Dictionary<string, GameObject> registeredObjects = new Dictionary<string, GameObject>();
    private float replicationTimer = 0f;

    // Replication actions enumeration
    public enum ReplicationAction
    {
        Update,     // Update existing object state
        Create,     // Instantiate new object
        Destroy,    // Destroy existing object
        Event       // Trigger event/action
    }

    private void Awake()
    {
        // Permitir múltiples instancias (una por Networking: Server y Client)
        // Solo mantener referencia a la última instancia de servidor
        if (Instance == null || isServer)
        {
            Instance = this;
        }
        
        Debug.Log("[ReplicationManager] Awake - Instance set");
    }

    private void Start()
    {
        networking = GetComponent<Networking>();
        if (networking == null)
        {
            Debug.LogError("[ReplicationManager] Networking component not found!");
        }
        else
        {
            Debug.Log($"[ReplicationManager] Networking component found in {(isServer ? "SERVER" : "CLIENT")} mode");
        }
    }

    public void Initialize(bool serverMode)
    {
        isServer = serverMode;
        Debug.Log($"[ReplicationManager] Initialized in {(isServer ? "SERVER" : "CLIENT")} mode");
    }

    private void Update()
    {
        if (!isServer) return;

        replicationTimer += Time.deltaTime;
        if (replicationTimer >= 1f / replicationRate)
        {
            ReplicateWorldState();
            replicationTimer = 0f;
        }
    }

    #region Player Registration
    
    /// Registers a player for replication tracking
    public void RegisterPlayer(int playerID, NetworkPlayer player)
    {
        if (!registeredPlayers.ContainsKey(playerID))
        {
            registeredPlayers[playerID] = player;
            Debug.Log($"[ReplicationManager] Player {playerID} registered");
        }
    }

    public void UnregisterPlayer(int playerID)
    {
        if (registeredPlayers.ContainsKey(playerID))
        {
            registeredPlayers.Remove(playerID);
            Debug.Log($"[ReplicationManager] Player {playerID} unregistered");
        }
    }

    public NetworkPlayer GetPlayerByID(int playerID)
    {
        return registeredPlayers.ContainsKey(playerID) ? registeredPlayers[playerID] : null;
    }

    #endregion

    #region Object Registration

    /// Registers a game object for replication
    public void RegisterObject(string objectID, GameObject obj)
    {
        if (!registeredObjects.ContainsKey(objectID))
        {
            registeredObjects[objectID] = obj;
            Debug.Log($"[ReplicationManager] Object {objectID} registered");
        }
    }

    public void UnregisterObject(string objectID)
    {
        if (registeredObjects.ContainsKey(objectID))
        {
            registeredObjects.Remove(objectID);
            Debug.Log($"[ReplicationManager] Object {objectID} unregistered");
        }
    }

    #endregion

    #region Server Replication (Passive Model)

    /// SERVER: Replicates complete world state to all clients
    /// This is the core of the Passive Replication model
    private void ReplicateWorldState()
    {
        if (!isServer || networking == null) return;

        // Replicate all registered players
        foreach (var kvp in registeredPlayers)
        {
            NetworkPlayer player = kvp.Value;
            if (player != null && player.isLocalPlayer)
            {
                ReplicatePlayerState(player);
            }
        }

        // Add more object replication here as needed
        // ReplicateGameObjects();
    }

    private void ReplicatePlayerState(NetworkPlayer player)
    {
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        PositionMessage posMsg = new PositionMessage(
            player.playerID,
            player.transform.position.x,
            player.transform.position.y,
            rb.linearVelocity.x,
            rb.linearVelocity.y
        );

        BroadcastReplicationPacket(posMsg, ReplicationAction.Update);
    }

    /// SERVER: Broadcasts a replication packet to all clients
    private void BroadcastReplicationPacket<T>(T message, ReplicationAction action) where T : NetworkMessage
    {
        byte[] data = NetworkSerializer.Serialize(message);
        if (data != null && networking != null)
        {
            networking.SendBytes(data);
        }
    }

    #endregion

    #region Client Replication Processing

    /// CLIENT: Processes received position updates
    public void ProcessPositionUpdate(PositionMessage posMsg)
    {
        if (isServer) return; // Server doesn't process its own updates

        NetworkPlayer player = GetPlayerByID(posMsg.playerID);
        if (player != null && !player.isLocalPlayer)
        {
            if (player.isPushed) return;

            Vector3 position = new Vector3(posMsg.posX, posMsg.posY, 0);
            Vector2 velocity = new Vector2 (posMsg.velX, posMsg.velY);
            player.UpdatePosition(position, velocity);
        }
    }

    /// CLIENT/SERVER: Processes key collection event
    public void ProcessKeyCollected(int playerID)
    {
        NetworkPlayer player = GetPlayerByID(playerID);
        if (player != null)
        {
            player.SetHasKey(true);
            Debug.Log($"[ReplicationManager] Player {playerID} collected key");
        }
    }

    /// CLIENT/SERVER: Processes key transfer between players
    public void ProcessKeyTransfer(int fromPlayerID, int toPlayerID)
    {
        NetworkPlayer fromPlayer = GetPlayerByID(fromPlayerID);
        NetworkPlayer toPlayer = GetPlayerByID(toPlayerID);

        if (fromPlayer != null && toPlayer != null)
        {
            fromPlayer.SetHasKey(false);
            toPlayer.SetHasKey(true);
            Debug.Log($"[ReplicationManager] Key transferred from Player {fromPlayerID} to Player {toPlayerID}");
        }
    }

    /// CLIENT: Processes push event - APPLIES TO THE PUSHED PLAYER
    public void ProcessPush(int playerID, Vector2 velocity, float duration)
    {
        NetworkPlayer player = GetPlayerByID(playerID);
        if (player != null)
        {
            Debug.Log($"[ReplicationManager] Applying PUSH to Player {playerID}: vel={velocity}, dur={duration} (isLocal: {player.isLocalPlayer})");
            
            // Aplicar empuje CON velocidad
            player.StartPush(velocity, duration);
        }
        else
        {
            Debug.LogError($"[ReplicationManager] Player {playerID} not found for push");
        }
    }

    #endregion

    #region Replication Events (Server Authority)

    /// SERVER: Replicates key collection to all clients
    public void ReplicateKeyCollection(int playerID)
    {
        if (!isServer) return;

        SimpleMessage keyMsg = new SimpleMessage("KEY_COLLECTED", playerID.ToString());
        byte[] data = NetworkSerializer.Serialize(keyMsg);
        
        if (data != null && networking != null)
        {
            networking.SendBytes(data);
        }

        // Also hide key for all clients
        SimpleMessage hideKeyMsg = new SimpleMessage("HIDE_KEY", "");
        byte[] hideData = NetworkSerializer.Serialize(hideKeyMsg);
        if (hideData != null)
        {
            networking.SendBytes(hideData);
        }

        Debug.Log($"[ReplicationManager] Replicated key collection for Player {playerID}");
    }

    /// SERVER: Replicates key transfer between players
    public void ReplicateKeyTransfer(int fromPlayerID, int toPlayerID)
    {
        if (!isServer) return;

        KeyTransferMessage transferMsg = new KeyTransferMessage(fromPlayerID, toPlayerID);
        byte[] data = NetworkSerializer.Serialize(transferMsg);
        
        if (data != null && networking != null)
        {
            networking.SendBytes(data);
        }

        Debug.Log($"[ReplicationManager] Replicated key transfer: {fromPlayerID} -> {toPlayerID}");
    }

    /// SERVER: Replicates push event to all clients
    public void ReplicatePush(int playerID, Vector2 velocity, float duration)
    {
        if (!isServer) return;

        PushMessage pushMsg = new PushMessage(playerID, velocity, duration);
        byte[] data = NetworkSerializer.Serialize(pushMsg);
        
        if (data != null && networking != null)
        {
            networking.SendBytes(data);
        }

        Debug.Log($"[ReplicationManager] Replicated push for Player {playerID}");
    }

    #endregion

    #region Debug Information

    public void PrintReplicationStats()
    {
        Debug.Log("=== REPLICATION STATS ===");
        Debug.Log($"Mode: {(isServer ? "SERVER" : "CLIENT")}");
        Debug.Log($"Replication Rate: {replicationRate} Hz");
        Debug.Log($"Registered Players: {registeredPlayers.Count}");
        Debug.Log($"Registered Objects: {registeredObjects.Count}");
        Debug.Log("========================");
    }

    #endregion
}