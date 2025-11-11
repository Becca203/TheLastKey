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
                networkPlayer.CollectKey();
                
                isCollected = true;
                gameObject.SetActive(false);
                
                Debug.Log("Player " + networkPlayer.playerID + " collected the key!");
            }
        }
    }
}