using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Player Prefabs")]
    public GameObject player1Prefab;
    public GameObject player2Prefab;

    [Header("Spawn Points")]
    public Transform player1SpawnPoint;
    public Transform player2SpawnPoint;

    [Header("Camera Prefab")]
    public GameObject playerCameraPrefab;

    [Header("Network")]
    public int localPlayerID = 0; 

    private GameObject localPlayerObject;
    private GameObject remotePlayerObject;
    private PlayerMovement2D localPlayerMovement;
    private NetworkPlayer localNetworkPlayer;
    private NetworkPlayer remoteNetworkPlayer;
    private GameObject localPlayerCameraObject;

    private UDPClient udpClient;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (udpClient != null)
            udpClient = FindAnyObjectByType<UDPClient>();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "GameScene")
        {
            InitializePlayers();
            SetupPlayerCamera();
        }
    }

    public void SetLocalPlayerID(int playerID)
    {
        localPlayerID = playerID;
    }

    private void InitializePlayers()
    {
        if (localPlayerID == 0)
        {
            Debug.LogWarning("Player ID not set yet, waiting...");
            return;
        }

        if (player1SpawnPoint == null)
        {
            GameObject spawn1 = GameObject.Find("Player1SpawnPoint");
            if (spawn1 != null) player1SpawnPoint = spawn1.transform;
        }

        if (player2SpawnPoint == null)
        {
            GameObject spawn2 = GameObject.Find("Player2SpawnPoint");
            if (spawn2 != null) player2SpawnPoint = spawn2.transform;
        }

        Vector3 p1Pos = player1SpawnPoint != null ? player1SpawnPoint.position : new Vector3(-2, 0, 0);
        Vector3 p2Pos = player2SpawnPoint != null ? player2SpawnPoint.position : new Vector3(2, 0, 0);

        GameObject player1Instance = Instantiate(player1Prefab, p1Pos, Quaternion.identity);
        GameObject player2Instance = Instantiate(player2Prefab, p2Pos, Quaternion.identity);

        player1Instance.name = "Player1";
        player2Instance.name = "Player2";

        // Determines which users is local and which is remote
        if (localPlayerID == 1)
        {
            localPlayerObject = player1Instance;
            remotePlayerObject = player2Instance;
        }
        else
        {
            localPlayerObject = player2Instance;
            remotePlayerObject = player1Instance;
        }

        SetupLocalPlayer();
        SetupRemotePlayer();
    }

    private void SetupLocalPlayer()
    {
        // Local player has control of movement
        localPlayerMovement = localPlayerObject.GetComponent<PlayerMovement2D>();
        if (localPlayerMovement == null)
            localPlayerMovement = localPlayerObject.AddComponent<PlayerMovement2D>();

        localPlayerMovement.enabled = true;

        localNetworkPlayer = localPlayerObject.GetComponent<NetworkPlayer>();
        if (localNetworkPlayer == null)
            localNetworkPlayer = localPlayerObject.AddComponent<NetworkPlayer>();

        localNetworkPlayer.isLocalPlayer = true;
        localNetworkPlayer.playerID = localPlayerID;

        Debug.Log("Local player configured: " + localPlayerObject.name);
    }

    private void SetupRemotePlayer()
    {
        PlayerMovement2D remoteMovement = remotePlayerObject.GetComponent<PlayerMovement2D>();
        if (remoteMovement != null)
            remoteMovement.enabled = false;

        remoteNetworkPlayer = remotePlayerObject.GetComponent<NetworkPlayer>();
        if (remoteNetworkPlayer == null)
            remoteNetworkPlayer = remotePlayerObject.AddComponent<NetworkPlayer>();

        remoteNetworkPlayer.isLocalPlayer = false;
        remoteNetworkPlayer.playerID = (localPlayerID == 1) ? 2 : 1;

        Debug.Log("Remote player configured: " + remotePlayerObject.name);
    }

    private void SetupPlayerCamera()
    {
        if (localPlayerObject == null || playerCameraPrefab == null)
        {
            Debug.LogWarning("Cannot setup camera: localPlayer or cameraPrefab is null");
            return;
        }

        // Instanciar la cámara del jugador local
        localPlayerCameraObject = Instantiate(playerCameraPrefab);
        localPlayerCameraObject.name = "PlayerCamera_Local";
        
        // Configurar el controlador de cámara
        PlayerCameraController cameraController = localPlayerCameraObject.GetComponent<PlayerCameraController>();
        if (cameraController != null)
        {
            cameraController.SetTarget(localPlayerObject.transform);
            
            // Opcional: configurar límites basados en el nivel
            // cameraController.SetBounds(new Vector2(-20, -10), new Vector2(20, 10));
        }
        
        Debug.Log("Player camera setup complete for local player");
    }

    public void UpdateRemotePlayerPosition(int playerID, Vector3 position, Vector2 velocity)
    {
        if (remoteNetworkPlayer != null && remoteNetworkPlayer.playerID == playerID)
        {
            remoteNetworkPlayer.UpdatePosition(position, velocity);
        }
    }
    
    public NetworkPlayer FindPlayerByID(int id)
    {
        if (localNetworkPlayer != null && localNetworkPlayer.playerID == id)
            return localNetworkPlayer;

        if (remoteNetworkPlayer != null && remoteNetworkPlayer.playerID == id)
            return remoteNetworkPlayer;

        return null;
    }
}