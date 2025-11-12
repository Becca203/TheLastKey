using UnityEngine;

/// <summary>
/// Singleton that manages the network role and persists across scenes
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    public enum NetworkRole
    {
        None,
        Host,     // Server + Client
        Client    // Client only
    }

    [Header("Network Configuration")]
    public NetworkRole currentRole = NetworkRole.None;
    public string serverIP = "127.0.0.1";
    public string playerName = "";
    public int serverPort = 9050;

    [Header("References")]
    private UDPServer serverInstance;
    private UDPClient clientInstance;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        Debug.Log("[NetworkManager] Initialized");
    }

    /// <summary>
    /// Starts the server and client (Host mode)
    /// </summary>
    public void StartAsHost(string hostName)
    {
        if (currentRole != NetworkRole.None)
        {
            Debug.LogWarning("[NetworkManager] Already initialized as " + currentRole);
            return;
        }

        currentRole = NetworkRole.Host;
        playerName = hostName;
        serverIP = "127.0.0.1"; // Host always connects to localhost

        Debug.Log($"[NetworkManager] Starting as HOST with name: {hostName}");

        // Create server first
        GameObject serverObj = new GameObject("UDPServer");
        serverObj.transform.SetParent(transform);
        serverInstance = serverObj.AddComponent<UDPServer>();

        // Wait a frame for server to initialize, then create client
        StartCoroutine(CreateClientAfterServerReady());
    }

    private System.Collections.IEnumerator CreateClientAfterServerReady()
    {
        // Wait 1 frame for server socket to bind
        yield return null;
        
        Debug.Log("[NetworkManager] Server ready, now creating client...");
        CreateClient();
    }

    /// <summary>
    /// Starts only the client (Join mode)
    /// </summary>
    public void StartAsClient(string clientName, string targetIP)
    {
        if (currentRole != NetworkRole.None)
        {
            Debug.LogWarning("[NetworkManager] Already initialized as " + currentRole);
            return;
        }

        currentRole = NetworkRole.Client;
        playerName = clientName;
        serverIP = targetIP;

        Debug.Log($"[NetworkManager] Starting as CLIENT with name: {clientName}, connecting to: {targetIP}");

        CreateClient();
    }

    private void CreateClient()
    {
        GameObject clientObj = new GameObject("UDPClient");
        clientObj.transform.SetParent(transform);
        clientInstance = clientObj.AddComponent<UDPClient>();
        
        // Configure client BEFORE calling Initialize
        clientInstance.serverIP = serverIP;
        clientInstance.username = playerName;

        // NOW manually initialize the client (this sends handshake)
        clientInstance.Initialize();
        
        Debug.Log($"[NetworkManager] Client created and initialized with username='{playerName}', serverIP='{serverIP}'");
    }

    public bool IsHost()
    {
        return currentRole == NetworkRole.Host;
    }

    public bool IsClient()
    {
        return currentRole == NetworkRole.Client;
    }

    public UDPClient GetClient()
    {
        return clientInstance;
    }

    public UDPServer GetServer()
    {
        return serverInstance;
    }

    public string GetLocalIPAddress()
    {
        if (serverInstance != null)
        {
            return serverInstance.GetServerIP();    
        }
        return "Not available";
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[NetworkManager] Shutting down...");
    }
}
