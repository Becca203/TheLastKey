using UnityEngine;

/// <summary>
/// Controla la cámara individual de cada jugador con capacidad de alternar a vista general
/// </summary>
public class PlayerCameraController : MonoBehaviour
{
    [Header("Camera References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Camera mainCamera;

    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Settings")]
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, -10f);
    [SerializeField] private float zoomedOrthographicSize = 3f;
    [SerializeField] private float defaultOrthographicSize = 5f;

    [Header("Bounds (Optional)")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 minBounds;
    [SerializeField] private Vector2 maxBounds;

    private bool usePlayerCamera = true;
    private NetworkPlayer networkPlayer;

    void Start()
    {
        // Obtener referencia al NetworkPlayer
        if (target != null)
        {
            networkPlayer = target.GetComponent<NetworkPlayer>();
        }

        // Configurar las cámaras al inicio
        SetupCameras();
    }

    void Update()
    {
        // Solo permitir el cambio de cámara si es el jugador local
        if (networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            HandleCameraSwitch();
        }

        // Seguir al objetivo si está usando la cámara del jugador
        if (usePlayerCamera && target != null)
        {
            FollowTarget();
        }
    }

    private void SetupCameras()
    {
        if (networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            // Jugador local: activar su cámara personal, desactivar main camera
            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                playerCamera.orthographicSize = zoomedOrthographicSize;
            }

            if (mainCamera != null)
            {
                mainCamera.enabled = false;
            }

            usePlayerCamera = true;
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

                Debug.Log($"Switched to {(usePlayerCamera ? "Player" : "Main")} Camera");
            }
        }
    }

    private void FollowTarget()
    {
        Vector3 desiredPosition = target.position + offset;

        // Aplicar límites si están habilitados
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
    /// Configura el objetivo de la cámara (llamado desde GameManager)
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
    /// Establece los límites de la cámara (útil para diferentes niveles)
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