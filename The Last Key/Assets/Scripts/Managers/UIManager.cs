using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUIController : MonoBehaviour
{
    [Header("UI Elements")]
    public Button saveButton;
    public Button loadButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI playerInfoText;

    private GameManager gameManager;
    private GameSaveSystem saveSystem;

    void Start()
    {
        // Buscar referencias
        gameManager = GameManager.Instance;
        saveSystem = GameSaveSystem.Instance;

        // Si no existe el sistema de guardado, crearlo
        if (saveSystem == null)
        {
            GameObject saveSystemObj = new GameObject("GameSaveSystem");
            saveSystem = saveSystemObj.AddComponent<GameSaveSystem>();
            DontDestroyOnLoad(saveSystemObj);
        }

        // Configurar botones
        if (saveButton != null)
            saveButton.onClick.AddListener(OnSaveButtonClicked);

        if (loadButton != null)
            loadButton.onClick.AddListener(OnLoadButtonClicked);

        UpdateUI();
    }

    void Update()
    {
        UpdatePlayerInfo();
    }

    private void OnSaveButtonClicked()
    {
        if (saveSystem != null)
        {
            saveSystem.SaveGame();
            UpdateStatus("Game saved!");
        }
        else
        {
            UpdateStatus("Save system not found!");
        }
    }

    private void OnLoadButtonClicked()
    {
        if (saveSystem != null)
        {
            bool success = saveSystem.LoadGame();
            if (success)
            {
                UpdateStatus("Game loaded!");
            }
            else
            {
                UpdateStatus("No save file found or load failed");
            }
        }
        else
        {
            UpdateStatus("Save system not found!");
        }
    }

    private void UpdateUI()
    {
        if (saveSystem != null && loadButton != null)
        {
            loadButton.interactable = saveSystem.HasSave();
        }

        UpdateStatus("Ready. Press F5 to save, F9 to load");
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log("UI Status: " + message);
    }

    private void UpdatePlayerInfo()
    {
        if (playerInfoText == null) return;

        string info = "";

        if (gameManager != null)
        {
            info += "Local Player: Player " + gameManager.localPlayerID + "\n\n";
        }

        // Mostrar info de todos los jugadores
        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            info += "Player " + player.playerID + "( " + (player.isLocalPlayer ? "LOCAL" : "REMOTE") + ")\n";
            info += $"Pos: {player.transform.position:F2}\n";
            if (rb != null)
            {
                info += $"Vel: {rb.linearVelocity:F2}\n";
            }
            info += "\n";
        }

        playerInfoText.text = info;
    }
}