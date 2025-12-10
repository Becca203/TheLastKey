using UnityEngine;

public class PlayerPushDetector : MonoBehaviour
{   
    [Header("Push Settings")]
    [SerializeField] private float pushRange = 1.5f;
    [SerializeField] private float pushCooldown = 0.5f;
    [SerializeField] private float pushForceHorizontal = 15f;
    [SerializeField] private float pushForceVertical = 20f;
    [SerializeField] private float pushGravity = 80f;
    [SerializeField] private float pushDuration = 0.5f;
    [SerializeField] private string playerTag = "Player";

    private NetworkPlayer networkPlayer;
    private float lastPushTime;
    private NetworkPlayer nearbyPlayer;
    
    // NEW: Track pushed player to send position updates
    private NetworkPlayer pushedPlayer = null;
    private float pushUpdateTimer = 0f;
    private float pushUpdateRate = 60f; // 60 updates per second

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
            Debug.Log($"[Push] LOCAL Player {networkPlayer.playerID} pushing REMOTE Player {targetPlayer.playerID}");
            
            ApplyPushForce(targetPlayer);
            networkPlayer.StealKey(targetPlayer.playerID);
            
            pushedPlayer = targetPlayer;
            pushUpdateTimer = 0f;
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

            Vector2 pushVelocity = new Vector2(
                horizontalDirection.x * pushForceHorizontal,
                pushForceVertical
            );

            // ✅ Aplicar localmente SOLO para feedback instantáneo del empujador
            targetPlayer.StartPush(pushVelocity, pushDuration);

            // ✅ Enviar al servidor para que lo replique
            SendPushMessage(targetPlayer.playerID, pushVelocity, pushDuration);

            Debug.Log($"[Push] Applied locally and sent push for Player {targetPlayer.playerID}");
        }
    }

    // NEW: Send position updates for the pushed player
    private void SendPushedPlayerPosition()
    {
        if (pushedPlayer == null) return;

        Networking networking = FindAnyObjectByType<Networking>();
        Rigidbody2D rb = pushedPlayer.GetComponent<Rigidbody2D>();

        if (networking != null && rb != null)
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
                networking.SendBytes(data);
            }
        }
    }

    private void SendPushMessage(int targetPlayerID, Vector2 velocity, float duration)
    {
        Networking networking = FindAnyObjectByType<Networking>();
        if (networking != null)
        {
            PushMessage pushMsg = new PushMessage(targetPlayerID, velocity, duration);
            byte[] data = NetworkSerializer.Serialize(pushMsg);
            
            if (data != null)
            {
                // Enviar 3 veces con pequeño delay para redundancia
                StartCoroutine(SendRedundantPush(networking, data, targetPlayerID));
            }
        }
    }

    private System.Collections.IEnumerator SendRedundantPush(Networking networking, byte[] data, int playerID)
    {
        for (int i = 0; i < 3; i++)
        {
            networking.SendBytes(data);
            Debug.Log($"[Push] Sent PUSH message #{i+1} for Player {playerID}");
            yield return new WaitForSeconds(0.016f); // ~16ms entre envíos
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