using System.Collections;
using UnityEngine;

public class CameraSequenceManager : MonoBehaviour
{
    [Header("Timing Settings")]
    [SerializeField] private float initialWaitTime = 3f;
    [SerializeField] private float transitionDuration = 2f;

    [Header("Transition Settings")]
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float zoomAmount = 0.5f; // Cuánto se reduce el tamaño ortográfico (zoom in)

    private Camera mainCamera;
    private bool sequenceStarted = false;
    private NetworkPlayer localPlayer;
    private PlayerCameraController localPlayerCamera;

    void Start()
    {
        // Buscar la Main Camera en la escena
        FindAndSetupMainCamera();

        // Esperar a que el jugador local esté listo
        StartCoroutine(WaitForLocalPlayerAndStartSequence());
    }

    private void FindAndSetupMainCamera()
    {
        GameObject mainCameraObj = GameObject.FindGameObjectWithTag("MainCamera");
        if (mainCameraObj == null)
        {
            mainCameraObj = GameObject.Find("Main Camera");
        }

        if (mainCameraObj != null)
        {
            mainCamera = mainCameraObj.GetComponent<Camera>();
            mainCamera.enabled = true;
            Debug.Log("[CameraSequenceManager] Main Camera found and enabled");
        }
        else
        {
            Debug.LogError("[CameraSequenceManager] Main Camera not found!");
        }
    }

    private IEnumerator WaitForLocalPlayerAndStartSequence()
    {
        // Esperar hasta que el jugador local esté instanciado
        while (localPlayer == null)
        {
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            foreach (NetworkPlayer player in players)
            {
                if (player.isLocalPlayer)
                {
                    localPlayer = player;
                    localPlayerCamera = player.GetComponent<PlayerCameraController>();

                    if (localPlayerCamera != null)
                    {
                        // Asegurarse de que la Player Camera esté desactivada al inicio
                        localPlayerCamera.SetCameraActive(false);
                        Debug.Log("[CameraSequenceManager] Local player found, player camera disabled");
                    }
                    break;
                }
            }
            yield return new WaitForSeconds(0.1f);
        }

        // Iniciar la secuencia de cámaras
        StartCoroutine(CameraSequence());
    }

    private IEnumerator CameraSequence()
    {
        if (sequenceStarted) yield break;
        sequenceStarted = true;

        Debug.Log("[CameraSequenceManager] Starting camera sequence - Main Camera active");

        // Paso 1: Esperar 3 segundos con la Main Camera activa
        yield return new WaitForSeconds(initialWaitTime);

        Debug.Log("[CameraSequenceManager] Starting transition to Player Camera");

        // Paso 2: Transición con zoom de 2 segundos
        if (localPlayerCamera != null && mainCamera != null)
        {
            Camera playerCamera = localPlayerCamera.GetPlayerCamera();

            if (playerCamera != null)
            {
                // Guardar posiciones y configuraciones iniciales
                Vector3 mainCameraStartPos = mainCamera.transform.position;
                Vector3 playerCameraTargetPos = localPlayer.transform.position;
                
                // Ajustar la posición objetivo de la cámara del jugador
                if (playerCamera != null)
                {
                    playerCameraTargetPos = playerCamera.transform.position;
                }

                // Valores iniciales para el zoom
                float mainCameraStartSize = mainCamera.orthographicSize;
                float targetSize = mainCamera.orthographicSize * zoomAmount;

                float elapsed = 0f;

                // Solo mantener Main Camera activa durante la transición
                mainCamera.enabled = true;
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                }

                // Realizar la transición de zoom y movimiento
                while (elapsed < transitionDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / transitionDuration;
                    float curveValue = transitionCurve.Evaluate(t);

                    // Interpolar posición hacia el jugador
                    mainCamera.transform.position = Vector3.Lerp(
                        mainCameraStartPos,
                        playerCameraTargetPos,
                        curveValue
                    );

                    // Interpolar zoom (orthographic size)
                    mainCamera.orthographicSize = Mathf.Lerp(
                        mainCameraStartSize,
                        targetSize,
                        curveValue
                    );

                    yield return null;
                }

                // Asegurar valores finales
                mainCamera.transform.position = playerCameraTargetPos;
                mainCamera.orthographicSize = targetSize;

                // Pequeña pausa antes del cambio final
                yield return new WaitForSeconds(0.1f);

                // Paso 3: Cambiar a la cámara del jugador
                mainCamera.enabled = false;
                
                if (playerCamera != null)
                {
                    playerCamera.enabled = true;
                }

                // Notificar al PlayerCameraController que ya puede usar su cámara
                localPlayerCamera.SetCameraActive(true);
                localPlayerCamera.SetUsePlayerCamera(true);

                // Restaurar el tamaño original de la Main Camera para futuras transiciones
                mainCamera.orthographicSize = mainCameraStartSize;
                mainCamera.transform.position = mainCameraStartPos;

                Debug.Log("[CameraSequenceManager] Transition complete - Player Camera active");
            }
        }
    }

    /// <summary>
    /// Forzar el cambio a Main Camera (llamado cuando alguien toca la puerta con la llave)
    /// </summary>
    public void SwitchToMainCamera()
    {
        StopAllCoroutines();

        if (mainCamera == null)
        {
            FindAndSetupMainCamera();
        }

        if (mainCamera != null)
        {
            mainCamera.enabled = true;
        }

        if (localPlayerCamera != null)
        {
            localPlayerCamera.SetCameraActive(false);
        }

        Debug.Log("[CameraSequenceManager] Forced switch to Main Camera");
    }
}