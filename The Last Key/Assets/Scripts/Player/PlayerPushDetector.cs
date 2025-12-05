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

    // NEW: For sending pushed player's position updates
    private NetworkPlayer pushedPlayer = null;
    private float pushUpdateTimer = 0f;
    private float pushUpdateRate = 60f; // Send 60 updates per second during push

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

        // NEW: Send position updates for pushed player
        if (pushedPlayer != null && pushedPlayer.isPushed)
        {
            pushUpdateTimer += Time.deltaTime;
            if (pushUpdateTimer >= 1f / pushUpdateRate)
            {
                SendPushedPlayerPosition();
                pushUpdateTimer = 0f;
            }
        }
        else
        {
            pushedPlayer = null;
        }
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
                Debug.Log("[Push] Push on cooldown!");
                return;
            }

            if (nearbyPlayer != null)
            {
                TryPushPlayer(nearbyPlayer);
                lastPushTime = Time.time;
            }
            else
            {
                Debug.Log("[Push] No player nearby to push!");
            }
        }
    }

    void TryPushPlayer(NetworkPlayer targetPlayer)
    {
        if (targetPlayer.hasKey && !networkPlayer.hasKey)
        {
            Debug.Log($"[Push] Pushing Player {targetPlayer.playerID} to steal the key!");

            ApplyPushForce(targetPlayer);
            networkPlayer.StealKey(targetPlayer.playerID);

            // NEW: Track pushed player to send position updates
            pushedPlayer = targetPlayer;
            pushUpdateTimer = 0f;
        }
        else if (!targetPlayer.hasKey)
        {
            Debug.Log($"[Push] Player {targetPlayer.playerID} doesn't have the key!");
        }
        else if (networkPlayer.hasKey)
        {
            Debug.Log("[Push] You already have the key!");
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

            Debug.Log($"[Push] Applying push to Player {targetPlayer.playerID}");
            Debug.Log($"[Push] Push velocity: {pushVelocity}");

            targetRb.linearVelocity = pushVelocity;

            targetPlayer.StartPush(pushDuration);
            StartCoroutine(ApplyPushGravity(targetRb, pushDuration));

            // Send push message to server
            SendPushMessage(targetPlayer.playerID, pushVelocity, pushDuration);
        }
    }

    // NEW: Send position updates for the pushed player
    private void SendPushedPlayerPosition()
    {
        if (pushedPlayer == null) return;

        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        Rigidbody2D rb = pushedPlayer.GetComponent<Rigidbody2D>();

        if (udpClient != null && rb != null)
        {
            PositionMessage posMsg = new PositionMessage(
                pushedPlayer.playerID,
                pushedPlayer.transform.position.x,
                pushedPlayer.transform.position.y,
                rb.linearVelocity.x,
                rb.linearVelocity.y
            );

            byte[] data = NetworkSerializer.Serialize(posMsg);
            if (data != null)
            {
                udpClient.SendBytes(data);
                // ADD THIS LOG TO VERIFY IT'S SENDING
                Debug.Log($"[Push] Sent position update for pushed Player {pushedPlayer.playerID}: pos=({pushedPlayer.transform.position.x:F2}, {pushedPlayer.transform.position.y:F2}), vel=({rb.linearVelocity.x:F2}, {rb.linearVelocity.y:F2})");
            }
        }
        else
        {
            Debug.LogError("[Push] Cannot send position: UDPClient or Rigidbody2D not found!");
        }
    }

    private void SendPushMessage(int targetPlayerID, Vector2 velocity, float duration)
    {
        UDPClient udpClient = FindAnyObjectByType<UDPClient>();
        if (udpClient != null)
        {
            PushMessage pushMsg = new PushMessage(targetPlayerID, velocity, duration);
            byte[] data = NetworkSerializer.Serialize(pushMsg);

            if (data != null)
            {
                udpClient.SendBytes(data);
                Debug.Log($"[Push] Sent PUSH message to server: targetID={targetPlayerID}, vel={velocity}, dur={duration}");
            }
        }
        else
        {
            Debug.LogError("[Push] UDPClient not found!");
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

        Debug.Log("[Push] Push gravity ended");

        // NEW: Stop tracking pushed player
        if (pushedPlayer != null && pushedPlayer.GetComponent<Rigidbody2D>() == rb)
        {
            pushedPlayer = null;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pushRange);
    }
}