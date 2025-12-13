using UnityEngine;
using System.Collections;

public class CameraSequenceManager : MonoBehaviour
{
    [Header("Timing Settings")]
    [SerializeField] private float initialWaitTime = 3f;
    [SerializeField] private float transitionDuration = 2f;

    [Header("Transition Settings")]
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

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

        // Paso 2: Transición de 2 segundos
        if (localPlayerCamera != null)
        {
            Camera playerCamera = localPlayerCamera.GetPlayerCamera();

            if (playerCamera != null && mainCamera != null)
            {
                // Activar ambas cámaras para la transición
                playerCamera.enabled = true;
                mainCamera.enabled = true;

                float elapsed = 0f;
                float mainCameraInitialDepth = mainCamera.depth;
                float playerCameraInitialDepth = playerCamera.depth;

                // Asegurarse de que Player Camera empiece por debajo
                playerCamera.depth = mainCameraInitialDepth - 1;

                while (elapsed < transitionDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / transitionDuration;
                    float curveValue = transitionCurve.Evaluate(t);

                    // Cambiar gradualmente la profundidad para simular transición
                    playerCamera.depth = Mathf.Lerp(mainCameraInitialDepth - 1, mainCameraInitialDepth + 1, curveValue);

                    yield return null;
                }

                // Paso 3: Desactivar Main Camera y dejar solo Player Camera
                mainCamera.enabled = false;
                playerCamera.depth = playerCameraInitialDepth;
                playerCamera.enabled = true;

                // Notificar al PlayerCameraController que ya puede usar su cámara
                localPlayerCamera.SetCameraActive(true);
                localPlayerCamera.SetUsePlayerCamera(true);

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