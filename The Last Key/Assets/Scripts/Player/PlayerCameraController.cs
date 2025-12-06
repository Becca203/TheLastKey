using UnityEngine;

/// Controla la c�mara individual de cada jugador con capacidad de alternar a vista general
public class PlayerCameraController : MonoBehaviour
{
    [Header("Camera References")]
    [SerializeField] private Camera playerCamera;
    private Camera mainCamera;

    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Settings")]
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, -10f);

    [Header("Bounds (Optional)")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 minBounds;
    [SerializeField] private Vector2 maxBounds;

    private bool usePlayerCamera = true;
    private NetworkPlayer networkPlayer;

    void Start()
    {
        FindMainCamera();

        if (target != null)
        {
            networkPlayer = target.GetComponent<NetworkPlayer>();
        }

        SetupCameras();
    }

    void Update()
    {
        // Solo permitir el cambio de c�mara si es el jugador local
        if (networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            HandleCameraSwitch();
        }

        // Seguir al objetivo si est� usando la c�mara del jugador
        if (usePlayerCamera && target != null)
        {
            FollowTarget();
        }
    }

    private void FindMainCamera()
    {
        GameObject mainCameraObj = GameObject.FindGameObjectWithTag("MainCamera");
        
        if (mainCameraObj != null)
        {
            mainCamera = mainCameraObj.GetComponent<Camera>();
            Debug.Log("[PlayerCameraController] Main Camera found and assigned");
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
            // Jugador local: activar su c�mara personal, desactivar main camera
            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                // NO modificar el orthographicSize - mantener el valor del prefab
            }

            if (mainCamera != null)
            {
                mainCamera.enabled = false;
            }

            usePlayerCamera = true;
            Debug.Log("[PlayerCameraController] Local player camera setup complete");
        }
        else
        {
            // Jugador remoto: desactivar su c�mara (no la necesita)
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
        if (playerCamera == null) return;

        Vector3 desiredPosition = target.position + offset;

        // Aplicar l�mites si est�n habilitados
        if (useBounds)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minBounds.x, maxBounds.x);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, minBounds.y, maxBounds.y);
        }

        // Suavizar el movimiento
        Vector3 smoothedPosition = Vector3.Lerp(
            playerCamera.transform.position,
            desiredPosition,
            followSpeed * Time.deltaTime
        );

        playerCamera.transform.position = smoothedPosition;
    }

    /// <summary>
    /// Configura el objetivo de la c�mara (llamado desde GameManager)
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            networkPlayer = target.GetComponent<NetworkPlayer>();
        }
    }

    /// <summary>
    /// Establece los l�mites de la c�mara (�til para diferentes niveles)
    /// </summary>
    public void SetBounds(Vector2 min, Vector2 max)
    {
        useBounds = true;
        minBounds = min;
        maxBounds = max;
    }

    void OnDrawGizmosSelected()
    {
        if (useBounds)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = new Vector3((minBounds.x + maxBounds.x) / 2f, (minBounds.y + maxBounds.y) / 2f, 0);
            Vector3 size = new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0);
            Gizmos.DrawWireCube(center, size);
        }
    }
}