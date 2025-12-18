using UnityEngine;

public class KeyBehaviour : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    private bool isCollected = false;
    private float cooldownTimer = 0f;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isCollected) return;
        if (Time.time < cooldownTimer) return;

        if (collision.CompareTag(playerTag))
        {
            NetworkPlayer networkPlayer = collision.GetComponent<NetworkPlayer>();
            
            if (networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                networkPlayer.CollectKey();
                
                isCollected = true;
                cooldownTimer = Time.time + 0.5f;
                gameObject.SetActive(false);
            }
        }
    }
    
    public void ShowKey()
    {
        isCollected = false;
        gameObject.SetActive(true);
        // Key shown again (server rejected collection) - no runtime log
    }
}