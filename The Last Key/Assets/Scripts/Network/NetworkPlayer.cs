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
                    interpolationSpeed * Time.deltaTime
                );
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

    // Updates the key status and visual overlay
    public void SetHasKey(bool value)
    {
        hasKey = value;

        if (playerMovement != null)
        {
            playerMovement.SetHasKey(value);
            Debug.Log("Updated key overlay for Player " + playerID + ": " + value);
        }
    }

    // Only the local player can collect the key
    public void CollectKey()
    {
        if (!isLocalPlayer) return;

        SetHasKey(true);
        SendKeyCollectedMessage();
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