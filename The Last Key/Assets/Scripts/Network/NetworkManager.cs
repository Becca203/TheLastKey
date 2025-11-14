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
        serverIP = "127.0.0.1";

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

        CreateClient();
    }

    private void CreateClient()
    {
        GameObject clientObj = new GameObject("UDPClient");
        clientObj.transform.SetParent(transform);
        clientInstance = clientObj.AddComponent<UDPClient>();
        
        // Configure client before initialization
        clientInstance.serverIP = serverIP;
        clientInstance.username = playerName;

        // Initialize the client (sends handshake)
        clientInstance.Initialize();
    }

    public void ResetNetwork()
    {
        if (serverInstance != null)
        {
            Destroy(serverInstance.gameObject);
            serverInstance = null;
        }

        if (clientInstance != null)
        {
            Destroy(clientInstance.gameObject);
            clientInstance = null;
        }

        currentRole = NetworkRole.None;
        serverIP = "127.0.0.1";
        playerName = "";
    }
}
