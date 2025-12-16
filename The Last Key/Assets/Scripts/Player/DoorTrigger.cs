using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField] private string winnerTag = "Player";
    [SerializeField] private string nextLevelName = "";

    private bool levelCompleted = false;
    private LevelTransitionUI transitionUI;

    private void Start()
    {
        transitionUI = FindAnyObjectByType<LevelTransitionUI>();
        if (transitionUI == null) 
            Debug.LogError("LevelTransitionUI not found in the scene.");

        if (string.IsNullOrEmpty(nextLevelName))
        {
            nextLevelName = GetNextLevelName();
            Debug.Log($"[DoorTrigger] Auto-detected next level: {nextLevelName}");
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (levelCompleted) return;

        if (collision.CompareTag(winnerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            
            if (networkPlayer != null && networkPlayer.hasKey)
            {
                Debug.Log("Player " + networkPlayer.playerID + " reached the door with the key!");

                levelCompleted = true;

                // ✅ Cambiar cámara localmente para TODOS los jugadores
                SwitchAllPlayersToMainCamera();

                // ✅ Enviar mensaje de cambio de cámara al otro jugador
                if (networkPlayer.isLocalPlayer)
                {
                    SendCameraSwitchMessage();
                    SendLevelCompletedMessage(networkPlayer.playerID);
                }

                if (transitionUI != null)
                    transitionUI.ShowPanel();
            }
            else if (networkPlayer != null && !networkPlayer.hasKey)
            {
                Debug.Log("Player " + networkPlayer.playerID + " doesn't have the key!");
            }
        }
    }

    private void SwitchAllPlayersToMainCamera()
    {
        Debug.Log("[DoorTrigger] Switching all players to Main Camera");

        // Buscar todos los jugadores y cambiar sus cámaras
        NetworkPlayer[] allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        
        foreach (NetworkPlayer player in allPlayers)
        {
            PlayerCameraController cameraController = player.GetComponent<PlayerCameraController>();
            if (cameraController != null)
            {
                cameraController.ForceMainCamera();
                Debug.Log($"[DoorTrigger] Switched Player {player.playerID} to Main Camera");
            }
        }

        // También notificar al CameraSequenceManager si existe
        CameraSequenceManager sequenceManager = FindAnyObjectByType<CameraSequenceManager>();
        if (sequenceManager != null)
        {
            sequenceManager.SwitchToMainCamera();
        }
    }

    private void SendCameraSwitchMessage()
    {
        Networking clientNetworking = FindClientNetworking();
        
        if (clientNetworking != null)
        {
            CameraSwitchMessage cameraMsg = new CameraSwitchMessage(true);
            byte[] data = NetworkSerializer.Serialize(cameraMsg);

            if (data != null)
            {
                clientNetworking.SendBytes(data);
                Debug.Log("[DoorTrigger] Sent CAMERA_SWITCH message to other player");
            }
        }
        else
        {
            Debug.LogError("[DoorTrigger] Client Networking not found!");
        }
    }

    private void SendLevelCompletedMessage(int playerID)
    {
        Networking clientNetworking = FindClientNetworking();

        if (clientNetworking != null)
        {
            SimpleMessage completeMsg = new SimpleMessage("LEVEL_COMPLETE", nextLevelName);
            byte[] data = NetworkSerializer.Serialize(completeMsg);

            if (data != null)
            {
                clientNetworking.SendBytesReliable(data, "LEVEL_COMPLETE");
                Debug.Log($"[DoorTrigger] Sent LEVEL_COMPLETE message for next level: {nextLevelName}");
            }
        }
        else
        {
            Debug.LogError("[DoorTrigger] Client Networking not found!");
        }
    }

    private Networking FindClientNetworking()
    {
        Networking[] allNetworkings = FindObjectsByType<Networking>(FindObjectsSortMode.None);
        
        foreach (Networking net in allNetworkings)
        {
            if (net.mode == Networking.NetworkMode.Client)
            {
                return net;
            }
        }

        return null;
    }

    private string GetNextLevelName()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        int nextSceneIndex = currentSceneIndex + 1;

        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(nextSceneIndex);
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            Debug.LogWarning("[DoorTrigger] No next level found in build settings!");
            return "MainMenu";
        }
    }
}