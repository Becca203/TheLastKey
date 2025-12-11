using UnityEngine;
using UnityEngine.UI;

public class TrapBehaviour : MonoBehaviour
{
    [Header("Trap Settings")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float activationDelay = 0.2f;
    [SerializeField] private GameObject trapPrefab;
    [SerializeField] private float spawnYOffset = 0.3f;

    [Header("UI Abilities")]
    [SerializeField] private Abilities abilitiesUI;

    private NetworkPlayer networkPlayer;
    private Collider2D playerCollider;
    private float cooldownTimer = 0f;
    private bool isOnCooldown = false;

    private int ownerPlayerID = -1;
    private bool isActive = false;
    private bool isTrapInstance = false;

    private void Start()
    {
        networkPlayer = GetComponent<NetworkPlayer>();
        
        if (networkPlayer != null)
        {
            playerCollider = GetComponent<Collider2D>();
            isTrapInstance = false;

            if (abilitiesUI == null)
                abilitiesUI = GetComponentInChildren<Abilities>(true);
        }
        else
        {
            isTrapInstance = true;
        }
    }

    private void Update()
    {
        if (isTrapInstance) return;
        if (!networkPlayer.isLocalPlayer) return;

        if (isOnCooldown)
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer <= 0f)
            {
                isOnCooldown = false;
                cooldownTimer = 0f;
            }
        }
        
        if (Input.GetKeyDown(abilitiesUI.ability1Key))
        {
            TryPlaceTrap();
        }
    }

    // Trap placement logic
    private void TryPlaceTrap()
    {
        if (isOnCooldown) 
        {
            Debug.Log("Trap is on cooldown. Please wait.");
            return;
        }

        Vector3 trapPosition = transform.position + Vector3.down * spawnYOffset;

        Debug.Log("Placing trap at position: " + trapPosition);

        SendPlaceTrapMessage(trapPosition);
        StartCooldown();
    }

    private void StartCooldown()
    {
        isOnCooldown = true;
        cooldownTimer = abilitiesUI.ability1Cooldown;
    }

    private void SendPlaceTrapMessage(Vector3 position)
    {
        Networking[] allNetworkings = FindObjectsByType<Networking>(FindObjectsSortMode.None);
        Networking clientNetworking = null;
        
        foreach (Networking net in allNetworkings)
        {
            if (net.mode == Networking.NetworkMode.Client)
            {
                clientNetworking = net;
                break;
            }
        }

        if (clientNetworking != null)
        {
            TrapPlacedMessage msg = new TrapPlacedMessage(
                networkPlayer.playerID,
                position.x,
                position.y
            );
            
            byte[] data = NetworkSerializer.Serialize(msg);
            
            if (data != null)
            {
                clientNetworking.SendBytes(data);
                Debug.Log($"[TrapBehaviour] Sent TRAP_PLACED message");
            }
        }
    }

    // Trap instance logic
    public void InitializeAsTrap(int playerID)
    {
        isTrapInstance = true;
        ownerPlayerID = playerID;
        Invoke(nameof(ActivateTrap), activationDelay);
        Debug.Log($"[TrapBehaviour] Trap initialized by player {playerID} at {transform.position}");
    }

    private void ActivateTrap()
    {
        isActive = true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isTrapInstance || !isActive) return;

        if (collision.CompareTag(playerTag))
        {
            NetworkPlayer hitPlayer = collision.GetComponent<NetworkPlayer>();
            if (hitPlayer != null && hitPlayer.isLocalPlayer)
            {
                Debug.Log($"[TrapBehaviour] Player {hitPlayer.playerID} hit by trap placed by player {ownerPlayerID}");
                SendTrapTriggeredMessage(hitPlayer.playerID);
                Destroy(gameObject);
            }
        }
    }

    private void SendTrapTriggeredMessage(int hitPlayerID)
    {
        Networking[] allNetworkings = FindObjectsByType<Networking>(FindObjectsSortMode.None);
        Networking clientNetworking = null;
        
        foreach (Networking net in allNetworkings)
        {
            if (net.mode == Networking.NetworkMode.Client)
            {
                clientNetworking = net;
                break;
            }
        }

        if (clientNetworking != null)
        {
            TrapTriggeredMessage msg = new TrapTriggeredMessage(hitPlayerID, transform.position);
            byte[] data = NetworkSerializer.Serialize(msg);
            
            if (data != null)
            {
                clientNetworking.SendBytes(data);
                Debug.Log($"[TrapBehaviour] Sent TRAP_TRIGGERED message for player {hitPlayerID}");
            }
        }
    }
}
