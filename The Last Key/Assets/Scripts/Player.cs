using UnityEngine;

public class Player : MonoBehaviour
{
    // Move in A and D
    // Space for jump
    // Left click for attack --> Push other player
    // If pushed & has key speed increases for 5 seconds

    [SerializeField] private float moveSpeed = 5f;
    private Rigidbody2D rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        float moveInput = 0f;

        if (Input.GetKey(KeyCode.A))
        {
            moveInput = -1f ;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            moveInput = 1f;
        }

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
        }
        else
        {
            transform.position += new Vector3(moveInput * moveSpeed * Time.deltaTime, 0, 0);
        }
    }
}
