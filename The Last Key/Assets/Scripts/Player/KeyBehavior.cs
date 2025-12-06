using UnityEngine;

public class KeyBehaviour : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    private bool isCollected = false;
    private float cooldownTimer = 0f;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isCollected) return;
        
        // Pequeño cooldown para evitar doble trigger
        if (Time.time < cooldownTimer) return;

        if (collision.CompareTag(playerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            
            if (networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                Debug.Log($"[KeyBehaviour] Player {networkPlayer.playerID} attempting to collect key");
                networkPlayer.CollectKey();
                
                isCollected = true;
                cooldownTimer = Time.time + 0.5f; // Cooldown de 0.5s
                gameObject.SetActive(false);
                
                Debug.Log($"[KeyBehaviour] Player {networkPlayer.playerID} collected the key locally!");
            }
        }
    }
    
    // Método para mostrar la llave de nuevo si el servidor rechaza
    public void ShowKey()
    {
        isCollected = false;
        gameObject.SetActive(true);
        Debug.Log("[KeyBehaviour] Key shown again (server rejected collection)");
    }
}