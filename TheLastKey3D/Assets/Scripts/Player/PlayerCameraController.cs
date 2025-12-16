using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private string playerCameraName = "PlayerCamera"; // Nombre de la cámara en el prefab
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, -10f);

    [Header("Bounds (Optional)")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 minBounds;
    [SerializeField] private Vector2 maxBounds;

    private Camera playerCamera;
    private Camera mainCamera;
    private Transform target;
    private bool usePlayerCamera = false;
    private bool canUseCamera = false;
    private NetworkPlayer networkPlayer;

    void Start()
    {
        target = transform; // El objetivo es el propio jugador
        networkPlayer = GetComponent<NetworkPlayer>();
        
        // Buscar las cámaras en la escena
        FindCameras();
        
        // Configurar las cámaras según si es jugador local o remoto
        SetupCameras();
    }

    void Update()
    {
        // Solo permitir el cambio de cámara si es el jugador local Y puede usar la cámara
        if (networkPlayer != null && networkPlayer.isLocalPlayer && canUseCamera)
        {
            HandleCameraSwitch();
        }

        // Seguir al objetivo si está usando la cámara del jugador
        if (usePlayerCamera && canUseCamera && target != null)
        {
            FollowTarget();
        }
    }

    private void FindCameras()
    {
        // Buscar la Player Camera dentro del jugador
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        foreach (Camera cam in cameras)
        {
            if (cam.gameObject.name.Contains(playerCameraName) || cam.gameObject.name == playerCameraName)
            {
                playerCamera = cam;
                Debug.Log($"[PlayerCameraController] Player Camera found: {cam.gameObject.name}");
                break;
            }
        }

        // Si no se encuentra en el jugador, buscar por nombre
        if (playerCamera == null)
        {
            GameObject playerCamObj = GameObject.Find(playerCameraName);
            if (playerCamObj != null)
            {
                playerCamera = playerCamObj.GetComponent<Camera>();
                Debug.Log($"[PlayerCameraController] Player Camera found by name: {playerCamObj.name}");
            }
        }

        // Buscar la Main Camera en la escena
        GameObject mainCameraObj = GameObject.FindGameObjectWithTag("MainCamera");
        if (mainCameraObj != null)
        {
            mainCamera = mainCameraObj.GetComponent<Camera>();
            Debug.Log("[PlayerCameraController] Main Camera found by tag");
        }
        else
        {
            mainCameraObj = GameObject.Find("Main Camera");
            if (mainCameraObj != null)
            {
                mainCamera = mainCameraObj.GetComponent<Camera>();
                Debug.Log("[PlayerCameraController] Main Camera found by name");
            }
            else
            {
                Debug.LogWarning("[PlayerCameraController] Main Camera not found in scene!");
            }
        }
    }

    private void SetupCameras()
    {
        if (networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            // Jugador local: DESACTIVAR su cámara personal al inicio
            // La secuencia de cámara la activará después
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
            }
            
            usePlayerCamera = false;
            canUseCamera = false; // No puede usar la cámara hasta que termine la secuencia
            Debug.Log("[PlayerCameraController] Local player camera setup - waiting for sequence");
        }
        else
        {
            // Jugador remoto: desactivar su cámara (no la necesita)
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
            }
        }
    }

    private void HandleCameraSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            usePlayerCamera = !usePlayerCamera;

            if (playerCamera != null && mainCamera != null)
            {
                playerCamera.enabled = usePlayerCamera;
                mainCamera.enabled = !usePlayerCamera;

                Debug.Log($"[PlayerCameraController] Switched to {(usePlayerCamera ? "Player" : "Main")} Camera");
            }
            else if (mainCamera == null)
            {
                Debug.LogWarning("[PlayerCameraController] Cannot switch: Main Camera not found!");
            }
        }
    }

    private void FollowTarget()
    {
        if (playerCamera == null || !playerCamera.enabled) return;

        Vector3 desiredPosition = target.position + offset;

        if (useBounds)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minBounds.x, maxBounds.x);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, minBounds.y, maxBounds.y);
        }

        playerCamera.transform.position = Vector3.Lerp(
            playerCamera.transform.position, 
            desiredPosition, 
            followSpeed * Time.deltaTime
        );
    }

    // ===== MÉTODOS PÚBLICOS PARA CONTROL EXTERNO =====

    /// <summary>
    /// Activa o desactiva la Player Camera
    /// </summary>
    public void SetCameraActive(bool active)
    {
        if (playerCamera != null)
        {
            playerCamera.enabled = active;
        }
        canUseCamera = active;
    }

    /// <summary>
    /// Establece si se debe usar la Player Camera o la Main Camera
    /// </summary>
    public void SetUsePlayerCamera(bool usePlayer)
    {
        usePlayerCamera = usePlayer;
        
        if (playerCamera != null && mainCamera != null)
        {
            playerCamera.enabled = usePlayer;
            mainCamera.enabled = !usePlayer;
        }
    }

    /// <summary>
    /// Obtiene la referencia a la Player Camera
    /// </summary>
    public Camera GetPlayerCamera()
    {
        return playerCamera;
    }

    /// <summary>
    /// Fuerza el cambio a Main Camera (usado cuando se toca la puerta)
    /// </summary>
    public void ForceMainCamera()
    {
        usePlayerCamera = false;
        canUseCamera = false;
        
        if (playerCamera != null)
        {
            playerCamera.enabled = false;
        }
        
        if (mainCamera != null)
        {
            mainCamera.enabled = true;
        }
        
        Debug.Log("[PlayerCameraController] Forced to Main Camera");
    }

    void OnDrawGizmosSelected()
    {
        if (useBounds)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = new Vector3((minBounds.x + maxBounds.x) / 2, (minBounds.y + maxBounds.y) / 2, 0);
            Vector3 size = new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0);
            Gizmos.DrawWireCube(center, size);
        }
    }
}