using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    public bool isLocalPlayer = false;
    public int playerID = 0;

    [Header("Network Settings")]
    [SerializeField] private float sendRate = 20f; // Enviar 20 veces por segundo
    [SerializeField] private float interpolationSpeed = 10f;

    private Rigidbody2D rb;
    private UDPClient udpClient;
    private float sendTimer = 0f;

    // Para interpolación suave del jugador remoto
    private Vector3 targetPosition;
    private Vector2 targetVelocity;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        udpClient = FindAnyObjectByType<UDPClient>();

        if (udpClient == null)
        {
            Debug.LogError("NetworkPlayer: No se encontró UDPClient!");
        }

        targetPosition = transform.position;
    }

    void Update()
    {
        if (isLocalPlayer)
        {
            // Enviar posición periódicamente
            sendTimer += Time.deltaTime;
            if (sendTimer >= 1f / sendRate)
            {
                SendPositionUpdate();
                sendTimer = 0f;
            }
        }
        else
        {
            // Interpolar posición del jugador remoto
            transform.position = Vector3.Lerp(
                transform.position,
                targetPosition,
                interpolationSpeed * Time.deltaTime
            );

            // Actualizar velocidad del Rigidbody para animaciones
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

        // Formato: POSITION:playerID:x:y:velX:velY
        string posMessage = string.Format("POSITION:{0}:{1:F3}:{2:F3}:{3:F3}:{4:F3}",
            playerID,
            transform.position.x,
            transform.position.y,
            rb.linearVelocity.x,
            rb.linearVelocity.y
        );

        udpClient.SendMessage(posMessage);
    }

    public void UpdatePosition(Vector3 position, Vector2 velocity)
    {
        targetPosition = position;
        targetVelocity = velocity;
    }

    // Para serialización JSON
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
        return new PlayerData(playerID, transform.position, rb != null ? rb.linearVelocity : Vector2.zero);
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