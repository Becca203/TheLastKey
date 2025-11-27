using UnityEngine;

/// <summary>
/// Establece automáticamente los límites de la cámara basándose en el nivel
/// </summary>
public class CameraBoundsSetter : MonoBehaviour
{
    [SerializeField] private Vector2 minBounds = new Vector2(-20, -10);
    [SerializeField] private Vector2 maxBounds = new Vector2(20, 10);

    void Start()
    {
        // Buscar la cámara del jugador local y aplicar límites
        PlayerCameraController[] cameras = FindObjectsByType<PlayerCameraController>(FindObjectsSortMode.None);

        foreach (var cam in cameras)
        {
            cam.SetBounds(minBounds, maxBounds);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Vector3 center = new Vector3((minBounds.x + maxBounds.x) / 2f, (minBounds.y + maxBounds.y) / 2f, 0);
        Vector3 size = new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0);
        Gizmos.DrawWireCube(center, size);
    }
}