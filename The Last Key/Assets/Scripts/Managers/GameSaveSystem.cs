using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class GameSaveSystem : MonoBehaviour
{
    public static GameSaveSystem Instance { get; private set; }

    private string saveFilePath;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ruta del archivo de guardado
        saveFilePath = Path.Combine(Application.persistentDataPath, "gamesave.json");
        Debug.Log("Save file path: " + saveFilePath);
    }

    [System.Serializable]
    public class GameSaveData
    {
        public List<NetworkPlayer.PlayerData> players = new List<NetworkPlayer.PlayerData>();
        public string timestamp;

        public GameSaveData()
        {
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    // Guardar estado del juego
    public void SaveGame()
    {
        try
        {
            GameSaveData saveData = new GameSaveData();

            // Buscar todos los NetworkPlayers en la escena
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);

            foreach (var player in players)
            {
                saveData.players.Add(player.GetPlayerData());
            }

            // Convertir a JSON
            string json = JsonUtility.ToJson(saveData, true);

            // Guardar en archivo
            File.WriteAllText(saveFilePath, json);

            Debug.Log("Game saved successfully! Players: " + saveData.players.Count);
            Debug.Log("Save content: " + json);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving game: " + e.Message);
        }
    }

    // Cargar estado del juego
    public bool LoadGame()
    {
        try
        {
            if (!File.Exists(saveFilePath))
            {
                Debug.LogWarning("No save file found at: " + saveFilePath);
                return false;
            }

            // Leer archivo
            string json = File.ReadAllText(saveFilePath);

            // Convertir desde JSON
            GameSaveData saveData = JsonUtility.FromJson<GameSaveData>(json);

            if (saveData == null || saveData.players == null || saveData.players.Count == 0)
            {
                Debug.LogWarning("Save data is empty or invalid");
                return false;
            }

            Debug.Log("Game loaded successfully! Timestamp: " + saveData.timestamp);

            // Buscar todos los NetworkPlayers en la escena
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);

            // Aplicar datos guardados
            foreach (var playerData in saveData.players)
            {
                foreach (var player in players)
                {
                    if (player.playerID == playerData.playerID)
                    {
                        player.LoadPlayerData(playerData);
                        Debug.Log($"Loaded data for Player {playerData.playerID}: Pos({playerData.posX}, {playerData.posY})");
                        break;
                    }
                }
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error loading game: " + e.Message);
            return false;
        }
    }

    // Eliminar archivo de guardado
    public void DeleteSave()
    {
        try
        {
            if (File.Exists(saveFilePath))
            {
                File.Delete(saveFilePath);
                Debug.Log("Save file deleted");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error deleting save: " + e.Message);
        }
    }

    // Verificar si existe un guardado
    public bool HasSave()
    {
        return File.Exists(saveFilePath);
    }

    // Para testing: Auto-guardar cada X segundos (opcional)
    [Header("Auto Save (Optional)")]
    public bool enableAutoSave = false;
    public float autoSaveInterval = 30f; // segundos
    private float autoSaveTimer = 0f;

    void Update()
    {
        if (enableAutoSave)
        {
            autoSaveTimer += Time.deltaTime;
            if (autoSaveTimer >= autoSaveInterval)
            {
                SaveGame();
                autoSaveTimer = 0f;
            }
        }

        // Atajo de teclado para testing
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SaveGame();
            Debug.Log("Manual save triggered (F5)");
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            LoadGame();
            Debug.Log("Manual load triggered (F9)");
        }
    }
}