using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement2D : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float velocidadMovimiento = 8f;
    [SerializeField] private float suavizadoMovimiento = 0.1f;

    [Header("Salto")]
    [SerializeField] private float fuerzaSalto = 12f;
    [SerializeField] private float gravedadCaida = 2.5f;
    [SerializeField] private float gravedadBajaSalto = 2f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody2D rb;
    private float movimientoHorizontal;
    private Vector2 velocidadActual;
    private bool enSuelo;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        // Input de movimiento usando el nuevo Input System
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            movimientoHorizontal = 0f;

            if (keyboard.aKey.isPressed)
                movimientoHorizontal = -1f;
            else if (keyboard.dKey.isPressed)
                movimientoHorizontal = 1f;

            // Check si está en el suelo
            enSuelo = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            // Input de salto
            if (keyboard.spaceKey.wasPressedThisFrame && enSuelo)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, fuerzaSalto);
            }

            // Mejor gravedad para un salto más realista
            if (rb.linearVelocity.y < 0)
            {
                // Cae más rápido
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (gravedadCaida - 1) * Time.deltaTime;
            }
            else if (rb.linearVelocity.y > 0 && !keyboard.spaceKey.isPressed)
            {
                // Si suelta el botón de salto, cae más rápido (salto variable)
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (gravedadBajaSalto - 1) * Time.deltaTime;
            }
        }
    }

    void FixedUpdate()
    {
        // Movimiento horizontal suavizado
        float velocidadObjetivo = movimientoHorizontal * velocidadMovimiento;
        rb.linearVelocity = Vector2.SmoothDamp(
            rb.linearVelocity,
            new Vector2(velocidadObjetivo, rb.linearVelocity.y),
            ref velocidadActual,
            suavizadoMovimiento
        );
    }

    void OnDrawGizmosSelected()
    {
        // Visualizar el groundCheck en el editor
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}