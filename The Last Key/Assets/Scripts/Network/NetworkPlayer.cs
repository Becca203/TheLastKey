using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    public bool isLocalPlayer = false;
    public int playerID = 0;

    [Header("Network Settings")]
    [SerializeField] private float sendRate = 20f;
    [SerializeField] private float interpolationSpeed = 10f;

    [Header("Key Status")]
    public bool hasKey = false;

    [Header("Push System")]
    public bool isPushed = false;
    private float pushRecoveryTime = 0f;

    private Rigidbody2D rb;
    private UDPClient udpClient;
    private PlayerMovement2D playerMovement;
    private float sendTimer = 0f;

    private Vector3 targetPosition;
    private Vector2 targetVelocity;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        udpClient = FindAnyObjectByType<UDPClient>();
        playerMovement = GetComponent<PlayerMovement2D>();

        if (udpClient == null)
        {
            Debug.LogError("UDPClient not found");
        }

        if (playerMovement == null)
        {
            Debug.LogError("PlayerMovement2D not found on Player " + playerID);
        }

        targetPosition = transform.position;
    }

    void Update()
    {
        if (isPushed && Time.time >= pushRecoveryTime)
        {
            isPushed = false;
            Debug.Log("Player " + playerID + " recovered from push");
        }

        if (isLocalPlayer)
        {
            sendTimer += Time.deltaTime;
            if (sendTimer >= 1f / sendRate)
            {
                SendPositionUpdate();
                sendTimer = 0f;
            }
        }
        else
        {
            if (!isPushed)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    interpolationSpeed * Time.deltaTime
                );

                if (rb != null)
                {
                    rb.linearVelocity = Vector2.Lerp(
                        rb.linearVelocity,
                        targetVelocity,
                        interpolationSpeed * Time.fixedDeltaTime
                    );
                }
            }
        }
    }

    private void SendPositionUpdate()
    {
        if (udpClient == null || rb == null) return;

        PositionMessage posMsg = new PositionMessage(
            playerID,
            transform.position.x,
            transform.position.y,
            rb.linearVelocity.x,
            rb.linearVelocity.y
        );

        byte[] data = NetworkSerializer.Serialize(posMsg);
        if (data != null)
        {
            udpClient.SendBytes(data);
        }
    }

    public void UpdatePosition(Vector3 position, Vector2 velocity)
    {
        targetPosition = position;
        targetVelocity = velocity;
    }

    public void SetHasKey(bool value)
    {
        bool previousValue = hasKey;
        hasKey = value;

        if (playerMovement != null)
        {
            playerMovement.SetHasKey(value);
            Debug.Log("Player " + playerID + " hasKey changed from " + previousValue + " to " + value);
        }
    }

    public void CollectKey()
    {
        if (!isLocalPlayer) return;

        SetHasKey(true);
        SendKeyCollectedMessage();
    }

    public void StealKey(int targetPlayerID)
    {
        if (!isLocalPlayer) return;

        Debug.Log("Player " + playerID + " stealing key from Player " + targetPlayerID);
        SendKeyTransferMessage(targetPlayerID, playerID);
    }

    public void StartPush(float duration)
    {
        isPushed = true;
        pushRecoveryTime = Time.time + duration;
        
        Debug.Log("Player " + playerID + " is pushed for " + duration + " seconds");
    }

    private void SendKeyCollectedMessage()
    {
        if (udpClient == null)
        {
            Debug.LogError("UDPClient not found!");
            return;
        }

        SimpleMessage keyMsg = new SimpleMessage("KEY_COLLECTED", playerID.ToString());
        byte[] data = NetworkSerializer.Serialize(keyMsg);

        if (data != null)
        {
            udpClient.SendBytes(data);
            Debug.Log("KEY_COLLECTED message sent for Player " + playerID);
        }
    }

    private void SendKeyTransferMessage(int fromPlayer, int toPlayer)
    {
        if (udpClient == null)
        {
            Debug.LogError("UDPClient not found!");
            return;
        }

        KeyTransferMessage transferMsg = new KeyTransferMessage(fromPlayer, toPlayer);
        byte[] data = NetworkSerializer.Serialize(transferMsg);

        if (data != null)
        {
            udpClient.SendBytes(data);
            Debug.Log("KEY_TRANSFER sent: from Player " + fromPlayer + " to Player " + toPlayer);
        }
    }

    [System.Serializable]
    public class PlayerData
    {
        public int playerID;
        public float posX;
        public float posY;
        public float velX;
        public float velY;

        public PlayerData(int id, Vector3 pos, Vector2 vel)
        {
            playerID = id;
            posX = pos.x;
            posY = pos.y;
            velX = vel.x;
            velY = vel.y;
        }

        public Vector3 GetPosition()
        {
            return new Vector3(posX, posY, 0);
        }

        public Vector2 GetVelocity()
        {
            return new Vector2(velX, velY);
        }
    }

    public PlayerData GetPlayerData()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        return new PlayerData(
            playerID,
            transform.position,
            rb != null ? rb.linearVelocity : Vector2.zero
        );
    }

    public void LoadPlayerData(PlayerData data)
    {
        transform.position = data.GetPosition();
        if (rb != null)
        {
            rb.linearVelocity = data.GetVelocity();
        }
    }
}