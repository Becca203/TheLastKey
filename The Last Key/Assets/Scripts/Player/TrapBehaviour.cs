using UnityEngine;
using UnityEngine.UI;

public class TrapBehaviour : MonoBehaviour
{
    [Header("Trap Settings")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float activationDelay = 0.2f;
    [SerializeField] private GameObject trapPrefab;
    [SerializeField] private float trapCooldown = 60f;
    [SerializeField] private KeyCode placeTrapKey = KeyCode.E;

    [Header("Cooldown UI")]
    [SerializeField] private Image cooldownFillImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private Color availableColor = Color.white;
    [SerializeField] private Color cooldownColor = Color.gray;

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
            UpdateCooldownUI();
        }
        else
        {
            isTrapInstance = true;
            if (cooldownFillImage != null) cooldownFillImage.gameObject.SetActive(false);
            if (iconImage != null) iconImage.gameObject.SetActive(false);
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
            UpdateCooldownUI();
        }
        else {UpdateCooldownUI();}
        
        if (Input.GetKeyDown(placeTrapKey))
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

        float offset = playerCollider != null ? playerCollider.bounds.extents.x + 1.5f : 1f;
        Vector3 trapPosition = transform.position - new Vector3(offset, 0, 0);

        Debug.Log("Placing trap at position: " + trapPosition);

        SendPlaceTrapMessage(trapPosition);
        StartCooldown();
    }

    private void StartCooldown()
    {
        isOnCooldown = true;
        cooldownTimer = trapCooldown;
        UpdateCooldownUI();
    }

    private void UpdateCooldownUI()
    {
        if (cooldownFillImage != null)
        {
            float fill = isOnCooldown ? 1f - (cooldownTimer / trapCooldown) : 1f;
            cooldownFillImage.fillAmount = fill;
        }

        if (iconImage != null)
        {
            iconImage.color = isOnCooldown ? cooldownColor : availableColor;
        }
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
