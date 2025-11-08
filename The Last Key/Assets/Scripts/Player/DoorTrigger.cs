using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField] private string winnerTag = "Player";

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag(winnerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                Debug.Log("Local player reached the door! Player " + networkPlayer.playerID + " wins!");
                SendGameOverMessage(networkPlayer.playerID);
            }
        }
    }

    private void SendGameOverMessage(int playerID)
    {
        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        if (udpClient != null)
        {
            SimpleMessage gameOverMsg = new SimpleMessage("GAME_OVER", playerID.ToString());
            byte[] data = NetworkSerializer.Serialize(gameOverMsg);

            if (data != null)
            {
                udpClient.SendBytes(data);
                Debug.Log("Sent GAME_OVER message to server");
                LoadGameOverScene(playerID);
            }
        }
        else
        {
            Debug.LogError("UDPClient not found!");
        }
    }

    private void LoadGameOverScene(int playerID)
    {
        PlayerPrefs.SetInt("WinnerPlayerID", playerID);
        PlayerPrefs.Save();
        
        SceneManager.LoadScene("GameOverScene");
    }
}
