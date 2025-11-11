using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField] private string winnerTag = "Player";
    private bool gameEnded = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (gameEnded) return;

        if (collision.CompareTag(winnerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            
            if (networkPlayer != null && networkPlayer.hasKey)
            {
                Debug.Log("Player " + networkPlayer.playerID + " reached the door with the key!");
                
                // Solo el jugador local envía el mensaje
                if (networkPlayer.isLocalPlayer)
                {
                    gameEnded = true;
                    SendGameOverMessage(networkPlayer.playerID);
                }
            }
            else if (networkPlayer != null)
            {
                Debug.Log("Player " + networkPlayer.playerID + " doesn't have the key!");
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
                Debug.Log("Sent GAME_OVER message to server for Player " + playerID);
            }
        }
        else
        {
            Debug.LogError("UDPClient not found!");
        }
    }
}