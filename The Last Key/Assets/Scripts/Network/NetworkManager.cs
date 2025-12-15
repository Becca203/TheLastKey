using UnityEngine;
using System.Collections;

/// Singleton that manages the network role and persists across scenes
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
    private Networking serverNetworking;  
    private Networking clientNetworking; 

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
        Debug.Log("[NetworkManager] Singleton initialized");
    }

    /// Starts the server and client (Host mode)
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

        Debug.Log($"[NetworkManager] Starting as Host with name: {hostName}");

        // Create server first
        GameObject serverObj = new GameObject("Networking_Server");
        serverObj.transform.SetParent(transform);
        serverNetworking = serverObj.AddComponent<Networking>();
        
        Debug.Log("[NetworkManager] Calling Initialize on server...");
        serverNetworking.Initialize(Networking.NetworkMode.Server, serverIP, hostName);

        Debug.Log("[NetworkManager] Server component created, waiting before creating client...");
        StartCoroutine(CreateClientAfterServerReady());
    }

    private IEnumerator CreateClientAfterServerReady()
    {
        Debug.Log("[NetworkManager] Waiting 0.5 seconds for server to initialize...");
        yield return new WaitForSeconds(0.5f);

        Debug.Log("[NetworkManager] Server ready, creating client...");
        CreateClient();
    }

    /// Starts only the client (Join mode)
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

        Debug.Log($"[NetworkManager] Starting as Client with name: {clientName}, connecting to: {targetIP}");
        CreateClient();
    }

    private void CreateClient()
    {
        if (clientNetworking != null)
        {
            Debug.LogWarning("[NetworkManager] Client already exists!");
            return;
        }

        GameObject clientObj = new GameObject("Networking_Client");
        clientObj.transform.SetParent(transform);
        clientNetworking = clientObj.AddComponent<Networking>();
        
        Debug.Log($"[NetworkManager] Calling Initialize on client (IP: {serverIP}, Name: {playerName})...");
        clientNetworking.Initialize(Networking.NetworkMode.Client, serverIP, playerName);
        Debug.Log($"[NetworkManager] Client created and connecting to {serverIP}");
    }

    public Networking GetNetworking()
    {
        // Return client networking (used by both Host and Client roles)
        return clientNetworking;
    }

    public bool IsConnectionFailed()
    {
        if (clientNetworking != null)
        {
            return clientNetworking.IsConnectionFailed();
        }
        return false;
    }

    public bool RetryClientConnection()
    {
        if (clientNetworking != null)
        {
            Debug.Log("[NetworkManager] Retrying client connection...");
            return clientNetworking.RetryConnection();
        }
        return false;
    }

    public void ResetNetwork()
    {
        Debug.Log("[NetworkManager] Resetting network...");

        if (serverNetworking != null)
        {
            Destroy(serverNetworking.gameObject);
            serverNetworking = null;
            Debug.Log("[NetworkManager] Server networking destroyed");
        }

        if (clientNetworking != null)
        {
            Destroy(clientNetworking.gameObject);
            clientNetworking = null;
            Debug.Log("[NetworkManager] Client networking destroyed");
        }

        currentRole = NetworkRole.None;
        serverIP = "127.0.0.1";
        playerName = "";

        Debug.Log("[NetworkManager] Network reset complete");
    }
}