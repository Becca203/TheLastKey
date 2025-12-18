using System.Collections;
using UnityEngine;

public class CameraSequenceManager : MonoBehaviour
{
    [Header("Timing Settings")]
    [SerializeField] private float initialWaitTime = 3f;
    [SerializeField] private float transitionDuration = 2f;

    [Header("Transition Settings")]
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float zoomAmount = 0.5f; // Cu�nto se reduce el tama�o ortogr�fico (zoom in)

    private Camera mainCamera;
    private bool sequenceStarted = false;
    private NetworkPlayer localPlayer;
    private PlayerCameraController localPlayerCamera;

    void Start()
    {
        FindAndSetupMainCamera();
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
        }
    }

    private IEnumerator WaitForLocalPlayerAndStartSequence()
    {
        // Wait until the local player is instantiated
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
                        localPlayerCamera.SetCameraActive(false);
                    }
                    break;
                }
            }
            yield return new WaitForSeconds(0.1f);
        }

        // Iniciar la secuencia de c�maras
        StartCoroutine(CameraSequence());
    }

    private IEnumerator CameraSequence()
    {
        if (sequenceStarted) yield break;
        sequenceStarted = true;

    // Starting camera sequence; no runtime logs

        // Paso 1: Esperar 3 segundos con la Main Camera activa
        yield return new WaitForSeconds(initialWaitTime);

    // Starting transition to player camera

        // Paso 2: Transici�n con zoom de 2 segundos
        if (localPlayerCamera != null && mainCamera != null)
        {
            Camera playerCamera = localPlayerCamera.GetPlayerCamera();

            if (playerCamera != null)
            {
                // Guardar posiciones y configuraciones iniciales
                Vector3 mainCameraStartPos = mainCamera.transform.position;
                Vector3 playerCameraTargetPos = localPlayer.transform.position;
                
                // Ajustar la posici�n objetivo de la c�mara del jugador
                if (playerCamera != null)
                {
                    playerCameraTargetPos = playerCamera.transform.position;
                }

                // Valores iniciales para el zoom
                float mainCameraStartSize = mainCamera.orthographicSize;
                float targetSize = mainCamera.orthographicSize * zoomAmount;

                float elapsed = 0f;

                // Solo mantener Main Camera activa durante la transici�n
                mainCamera.enabled = true;
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                }

                // Perform zoom and movement transition
                while (elapsed < transitionDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / transitionDuration;
                    float curveValue = transitionCurve.Evaluate(t);

                    // Interpolar posici�n hacia el jugador
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

                // Brief pause before final switch
                yield return new WaitForSeconds(0.1f);

                // Paso 3: Cambiar a la c�mara del jugador
                mainCamera.enabled = false;
                
                if (playerCamera != null)
                {
                    playerCamera.enabled = true;
                }

                // Notify PlayerCameraController that the player camera can be used
                localPlayerCamera.SetCameraActive(true);
                localPlayerCamera.SetUsePlayerCamera(true);

                // Restore original main camera size and position for future transitions
                mainCamera.orthographicSize = mainCameraStartSize;
                mainCamera.transform.position = mainCameraStartPos;
                // Transition complete; no runtime log
            }
        }
    }

    /// <summary>
    /// Force switching back to the main camera (called when a door is interacted with using the key)
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

        // Forced switch to main camera; no runtime log
    }
}