using UnityEngine;

public class KeyBehaviour : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    private bool isCollected = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isCollected) return;

        if (collision.CompareTag(playerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                Debug.Log("Jugador " + networkPlayer.playerID + " ha recogido la llave!");

                // Activamos el estado local y overlay
                PlayerMovement2D playerMovement = collision.GetComponent<PlayerMovement2D>();
                if (playerMovement != null)
                {
                    playerMovement.SetHasKey(true);
                }

                // Enviamos mensaje al servidor
                SendKeyCollectedMessage(networkPlayer.playerID);

                // Desactivamos la llave visualmente
                isCollected = true;
            }
        }
    }

    private void SendKeyCollectedMessage(int playerID)
    {
        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        if (udpClient != null)
        {
            SimpleMessage keyMsg = new SimpleMessage("KEY_COLLECTED", playerID.ToString());
            byte[] data = NetworkSerializer.Serialize(keyMsg);

            if (data != null)
            {
                udpClient.SendBytes(data);
                Debug.Log("Mensaje KEY_COLLECTED enviado al servidor");
            }
        }
        else
        {
            Debug.LogError("UDPClient no encontrado!");
        }
    }
}