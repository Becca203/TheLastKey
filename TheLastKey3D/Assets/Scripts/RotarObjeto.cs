using UnityEngine;

public class RotarObjeto : MonoBehaviour
{
    // Velocidad de rotación (grados por segundo)
    public float velocidadRotacion = 100f;

    // Update se llama una vez por fotograma
    void Update()
    {
        // Rotamos el objeto sobre su eje Y, X y Z
        transform.Rotate(Vector3.up * velocidadRotacion * Time.deltaTime);
    }
}
