using UnityEngine;
using System.Collections;

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
        GameObject serverObj = new GameObject("Networking_Server");
        serverObj.transform.SetParent(transform);
        serverNetworking = serverObj.AddComponent<Networking>();
        serverNetworking.Initialize(Networking.NetworkMode.Server, serverIP, hostName);

        StartCoroutine(CreateClientAfterServerReady());
    }

    private IEnumerator CreateClientAfterServerReady()
    {
        yield return new WaitForSeconds(0.5f);

        Debug.Log("[NetworkManager] Server ready, creating client...");
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
        if (clientNetworking != null) return; 

        GameObject clientObj = new GameObject("Networking_Client");
        clientObj.transform.SetParent(transform);
        clientNetworking = clientObj.AddComponent<Networking>();
        clientNetworking.Initialize(Networking.NetworkMode.Client, serverIP, playerName);
        Debug.Log($"[NetworkManager] Client created and connecting to {serverIP}");
    }

    public void ResetNetwork()
    {
        if (serverNetworking != null)
        {
            Destroy(serverNetworking.gameObject);
            serverNetworking = null;
        }

        if (clientNetworking != null)
        {
            Destroy(clientNetworking.gameObject);
            clientNetworking = null;
        }

        currentRole = NetworkRole.None;
        serverIP = "127.0.0.1";
        playerName = "";

        Debug.Log("[NetworkManager] Network reset complete");
    }
}
