using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement2D : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float movementSpeed = 8f;
    [SerializeField] private float movementSmoothing = 0.1f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float fallGravity = 2.5f;
    [SerializeField] private float lowJumpGravity = 2f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody2D rb;
    private float horizontalMovement;
    private Vector2 currentVelocity;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        // Movement input using the new Input System
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        horizontalMovement = 0f;

        if (keyboard.aKey.isPressed)
            horizontalMovement = -1f;
        else if (keyboard.dKey.isPressed)
            horizontalMovement = 1f;

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Jump input
        if (keyboard.spaceKey.wasPressedThisFrame && isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }

        // Better gravity for more realistic jump
        if (rb.linearVelocity.y < 0)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallGravity - 1) * Time.deltaTime;
        }
        else if (rb.linearVelocity.y > 0 && !keyboard.spaceKey.isPressed)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (lowJumpGravity - 1) * Time.deltaTime;
        }
    }

    void FixedUpdate()
    {
        // Smoothed horizontal movement
        float targetVelocity = horizontalMovement * movementSpeed;
        rb.linearVelocity = Vector2.SmoothDamp(
            rb.linearVelocity,
            new Vector2(targetVelocity, rb.linearVelocity.y),
            ref currentVelocity,
            movementSmoothing
        );
    }

    void OnDrawGizmosSelected()
    {
        // Visualize groundCheck in the editor
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}