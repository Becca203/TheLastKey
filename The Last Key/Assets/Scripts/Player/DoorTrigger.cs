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

                if (networkPlayer.isLocalPlayer)
                    SendLevelCompletedMessage(networkPlayer.playerID);

                if (transitionUI != null)
                    transitionUI.ShowPanel();
            }
            else if (networkPlayer != null && !networkPlayer.hasKey)
            {
                Debug.Log("Player " + networkPlayer.playerID + " doesn't have the key!");
            }
        }
    }

    private void SendLevelCompletedMessage(int playerID)
    {
         Networking networking = FindAnyObjectByType<Networking>();
        if (networking != null)
        {
            SimpleMessage completeMsg = new SimpleMessage("LEVEL_COMPLETE", nextLevelName);
            byte[] data = NetworkSerializer.Serialize(completeMsg);

            if (data != null)
            {
                networking.SendBytes(data);
                Debug.Log($"[DoorTrigger] Sent LEVEL_COMPLETE message for next level: {nextLevelName}");
            }
        }
        else
        {
            Debug.LogError("[DoorTrigger] Networking component not found!");
        }
    }

    private string GetNextLevelName()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == "GameScene")
        {
            return "Level2";
        }

        if (currentScene.StartsWith("Level"))
        {
            string numberPart = currentScene.Replace("Level", "");
            if (int.TryParse(numberPart, out int levelNumber))
            {
                return $"Level{levelNumber + 1}";
            }
        }

        // Fallback: try GameScene (Level1)
        Debug.LogWarning($"[DoorTrigger] Could not determine next level from '{currentScene}', defaulting to Level1");
        return "GameScene";
    }
}