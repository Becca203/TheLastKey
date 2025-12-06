using UnityEngine;

public class KeyAuraEffect : MonoBehaviour
{
    [Header("Aura Settings")]
    [SerializeField] private Color auraColor = new Color(1f, 0.92f, 0.016f, 0.7f);
    [SerializeField] private int blurLayers = 7;
    [SerializeField] private float maxScale = 1.15f;
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float pulseAmount = 0.1f;

    private SpriteRenderer mainSprite;
    private SpriteRenderer playerSprite;
    private SpriteRenderer[] blurSprites;

    void Awake()
    {
        mainSprite = GetComponent<SpriteRenderer>();
        playerSprite = GetComponentInParent<SpriteRenderer>();

        if (mainSprite == null)
        {
            Debug.LogError("KeyAuraEffect: No se encontró SpriteRenderer en este GameObject!");
            enabled = false;
            return;
        }

        if (playerSprite == null)
        {
            Debug.LogError("KeyAuraEffect: No se encontró SpriteRenderer del jugador en el padre!");
            enabled = false;
            return;
        }

        mainSprite.enabled = false;
    }

    void CreateBlurLayers()
    {
        if (blurSprites != null)
        {
            foreach (var sr in blurSprites)
            {
                if (sr != null && sr.gameObject != null)
                    Destroy(sr.gameObject);
            }
        }

        blurSprites = new SpriteRenderer[blurLayers];

        for (int i = 0; i < blurLayers; i++)
        {
            GameObject layer = new GameObject("BlurLayer_" + i);
            layer.transform.SetParent(transform);
            layer.transform.localPosition = Vector3.zero;
            layer.transform.localRotation = Quaternion.identity;

            SpriteRenderer sr = layer.AddComponent<SpriteRenderer>();

            sr.sprite = playerSprite.sprite;
            sr.sortingLayerName = mainSprite.sortingLayerName;
            sr.sortingOrder = mainSprite.sortingOrder - (i + 1);

            float normalizedIndex = (i + 1f) / blurLayers;
            float scale = 1f + (maxScale - 1f) * normalizedIndex;
            layer.transform.localScale = Vector3.one * scale;

            Color layerColor = auraColor;
            layerColor.a = auraColor.a * (1f - Mathf.Pow(normalizedIndex, 1.2f));
            sr.color = layerColor;

            blurSprites[i] = sr;
        }

        Debug.Log("KeyAuraEffect: Created " + blurLayers + " blur layers");
    }

    void Update()
    {
        if (blurSprites == null || blurSprites.Length == 0) return;

        if (playerSprite != null && blurSprites[0].sprite != playerSprite.sprite)
        {
            foreach (SpriteRenderer sr in blurSprites)
            {
                if (sr != null)
                    sr.sprite = playerSprite.sprite;
            }
        }

        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;

        for (int i = 0; i < blurSprites.Length; i++)
        {
            if (blurSprites[i] == null) continue;

            float normalizedIndex = (i + 1f) / blurLayers;
            float baseScale = 1f + (maxScale - 1f) * normalizedIndex;
            float finalScale = baseScale + pulse;
            blurSprites[i].transform.localScale = Vector3.one * finalScale;
        }
    }

    void OnEnable()
    {
        if (playerSprite == null)
            playerSprite = GetComponentInParent<SpriteRenderer>();

        if (mainSprite == null)
            mainSprite = GetComponent<SpriteRenderer>();

        CreateBlurLayers();
        Debug.Log("KeyAuraEffect: OnEnable - Aura activada");
    }

    void OnDisable()
    {
        if (blurSprites != null)
        {
            foreach (var sr in blurSprites)
            {
                if (sr != null && sr.gameObject != null)
                    Destroy(sr.gameObject);
            }
            blurSprites = null;
        }
        Debug.Log("KeyAuraEffect: OnDisable - Aura desactivada");
    }
}