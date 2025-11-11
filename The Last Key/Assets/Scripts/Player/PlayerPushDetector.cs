using UnityEngine;

public class PlayerPushDetector : MonoBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private float pushRange = 1.5f;
    [SerializeField] private float pushCooldown = 0.5f;
    [SerializeField] private float pushForceHorizontal = 20f;
    [SerializeField] private float pushForceVertical = 25f;
    [SerializeField] private float pushGravity = 80f;
    [SerializeField] private float pushDuration = 0.8f;
    [SerializeField] private string playerTag = "Player";

    private NetworkPlayer networkPlayer;
    private float lastPushTime;
    private NetworkPlayer nearbyPlayer;

    void Start()
    {
        networkPlayer = GetComponent<NetworkPlayer>();
        if (networkPlayer == null)
        {
            Debug.LogError("PlayerPushDetector requires NetworkPlayer component!");
            enabled = false;
        }
    }

    void Update()
    {
        if (!networkPlayer.isLocalPlayer) return;

        DetectNearbyPlayer();
        HandlePushInput();
    }

    void DetectNearbyPlayer()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, pushRange);
        nearbyPlayer = null;

        foreach (Collider2D col in colliders)
        {
            if (col.gameObject == gameObject) continue;
            
            if (col.CompareTag(playerTag))
            {
                NetworkPlayer otherPlayer = col.GetComponent<NetworkPlayer>();
                if (otherPlayer != null && !otherPlayer.isLocalPlayer)
                {
                    nearbyPlayer = otherPlayer;
                    break;
                }
            }
        }
    }

    void HandlePushInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time - lastPushTime < pushCooldown)
            {
                Debug.Log("Push on cooldown!");
                return;
            }

            if (nearbyPlayer != null)
            {
                TryPushPlayer(nearbyPlayer);
                lastPushTime = Time.time;
            }
            else
            {
                Debug.Log("No player nearby to push!");
            }
        }
    }

    void TryPushPlayer(NetworkPlayer targetPlayer)
    {
        if (targetPlayer.hasKey && !networkPlayer.hasKey)
        {
            Debug.Log("Pushing Player " + targetPlayer.playerID + " to steal the key!");
            
            ApplyPushForce(targetPlayer);
            networkPlayer.StealKey(targetPlayer.playerID);
        }
        else if (!targetPlayer.hasKey)
        {
            Debug.Log("Player " + targetPlayer.playerID + " doesn't have the key!");
        }
        else if (networkPlayer.hasKey)
        {
            Debug.Log("You already have the key!");
        }
    }

    void ApplyPushForce(NetworkPlayer targetPlayer)
    {
        Rigidbody2D targetRb = targetPlayer.GetComponent<Rigidbody2D>();
        if (targetRb != null)
        {
            Vector2 horizontalDirection = (targetPlayer.transform.position - transform.position).normalized;
            
            targetRb.linearVelocity = Vector2.zero;
            
            Vector2 pushVelocity = new Vector2(
                horizontalDirection.x * pushForceHorizontal,
                pushForceVertical
            );
            
            targetRb.linearVelocity = pushVelocity;
            
            targetPlayer.StartPush(pushDuration);
            StartCoroutine(ApplyPushGravity(targetRb, pushDuration));
            
            Debug.Log($"Applied push to Player {targetPlayer.playerID} - Velocity: {pushVelocity}");
        }
    }

    private System.Collections.IEnumerator ApplyPushGravity(Rigidbody2D rb, float duration)
    {
        float elapsed = 0f;
        
        while (elapsed < duration && rb != null)
        {
            Vector2 currentVel = rb.linearVelocity;
            currentVel.y -= pushGravity * Time.fixedDeltaTime;
            rb.linearVelocity = currentVel;
            
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        
        Debug.Log("Push gravity ended");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pushRange);
    }
}