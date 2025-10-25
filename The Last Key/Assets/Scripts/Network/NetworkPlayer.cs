using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    public bool isLocalPlayer = false;
    public int playerID = 0;

    [Header("Network Settings")]
    [SerializeField] private float sendRate = 20f;
    [SerializeField] private float interpolationSpeed = 10f;

    private Rigidbody2D rb;
    private UDPClient udpClient;
    private float sendTimer = 0f;

    private Vector3 targetPosition;
    private Vector2 targetVelocity;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        udpClient = FindAnyObjectByType<UDPClient>();

        if (udpClient == null)
        {
            Debug.LogError("UDPClient not found");
        }

        targetPosition = transform.position;
    }

    // Handle network updates: send position if local, interpolate if remote
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

    // Serialize and send current position and velocity to server
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