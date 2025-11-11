using UnityEngine;

public class KeyAuraEffect : MonoBehaviour
{
    [Header("Aura Settings")]
    [SerializeField] private Color auraColor = new Color(1f, 0.92f, 0.016f, 0.6f);
    [SerializeField] private int blurLayers = 4;
    [SerializeField] private float maxScale = 1.25f;
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float pulseAmount = 0.15f;

    private SpriteRenderer mainSprite;
    private SpriteRenderer playerSprite;
    private SpriteRenderer[] blurSprites;

    void Awake()
    {
        mainSprite = GetComponent<SpriteRenderer>();
        playerSprite = GetComponentInParent<SpriteRenderer>();

        if (mainSprite == null)
        {
            Debug.LogError("KeyAuraEffect: No se encontr� SpriteRenderer en este GameObject!");
            enabled = false;
            return;
        }

        if (playerSprite == null)
        {
            Debug.LogError("KeyAuraEffect: No se encontr� SpriteRenderer del jugador en el padre!");
            enabled = false;
            return;
        }

        CreateBlurLayers();
    }

    void CreateBlurLayers()
    {
        blurSprites = new SpriteRenderer[blurLayers];

        for (int i = 0; i < blurLayers; i++)
        {
            GameObject layer = new GameObject("BlurLayer_" + i);
            layer.transform.SetParent(transform);
            layer.transform.localPosition = Vector3.zero;
            layer.transform.localRotation = Quaternion.identity;

            SpriteRenderer sr = layer.AddComponent<SpriteRenderer>();

            // Uses the player's sprite
            sr.sprite = playerSprite.sprite;
            sr.sortingLayerName = mainSprite.sortingLayerName;
            sr.sortingOrder = mainSprite.sortingOrder - (i + 1);

            // Progressive scale
            float scale = 1f + (maxScale - 1f) * ((i + 1f) / blurLayers);
            layer.transform.localScale = Vector3.one * scale;

            // Color with progressive transparency
            Color layerColor = auraColor;
            layerColor.a = auraColor.a * (1f - ((float)i / blurLayers));
            sr.color = layerColor;

            blurSprites[i] = sr;
        }

        // Hides the main sprite of the CheckKey
        mainSprite.enabled = false;
    }

    void Update()
    {
        if (blurSprites == null || blurSprites.Length == 0) return;

        // Updates the sprite if the player changes animation
        if (playerSprite != null && blurSprites[0].sprite != playerSprite.sprite)
        {
            foreach (SpriteRenderer sr in blurSprites)
            {
                sr.sprite = playerSprite.sprite;
            }
        }

        // Pulse effect
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;

        for (int i = 0; i < blurSprites.Length; i++)
        {
            float normalizedIndex = (i + 1f) / blurLayers;
            float baseScale = 1f + (maxScale - 1f) * Mathf.Pow(normalizedIndex, 1.5f);
            float finalScale = baseScale + pulse;
            blurSprites[i].transform.localScale = Vector3.one * finalScale;
        }
    }

    void OnEnable()
    {
        // Recreates the layers if the GameObject is reactivated
        if (blurSprites == null || blurSprites.Length == 0)
        {
            if (playerSprite == null)
                playerSprite = GetComponentInParent<SpriteRenderer>();

            if (mainSprite == null)
                mainSprite = GetComponent<SpriteRenderer>();

            CreateBlurLayers();
        }
    }

    void OnDisable()
    {
        // Cleans layers when deactivated
        if (blurSprites != null)
        {
            foreach (var sr in blurSprites)
            {
                if (sr != null && sr.gameObject != null)
                    Destroy(sr.gameObject);
            }
        }
    }
}