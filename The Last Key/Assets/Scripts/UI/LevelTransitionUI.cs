using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelTransitionUI : MonoBehaviour
{
    public GameObject panel;
    public TextMeshProUGUI statusText;
    public Button continueButton;
    public Button returnButton;

    private Networking networking;
    private bool hasVoted = false;
    private float previousTimeScale = 1f;

    private void Start()
    {
        if (panel != null) 
            panel.SetActive(false);
        if (continueButton != null) 
            continueButton.onClick.AddListener(OnContinueClicked);
        if (returnButton != null) 
            returnButton.onClick.AddListener(OnReturnClicked);

        // Find Client mode Networking component
        Networking[] allNetworkings = FindObjectsByType<Networking>(FindObjectsSortMode.None);
        foreach (Networking net in allNetworkings)
        {
            if (net.mode == Networking.NetworkMode.Client)
            {
                networking = net;
                Debug.Log("[LevelTransitionUI] Found Client Networking component");
                break;
            }
        }

        if (networking == null)
        {
            Debug.LogError("[LevelTransitionUI] No Client Networking component found!");
        }
    }

    public void ShowPanel()
    {
        if (panel != null)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            panel.SetActive(true);
            hasVoted = false;
            UpdateStatusText("Waiting for your decision...");
            EnableButtons();
        }
    }

    private void OnContinueClicked()
    {
        if (hasVoted) return;
        hasVoted = true;
        UpdateStatusText("Waiting for other player...");
        DisableButtons();
        SendVote(true);
    }

    private void OnReturnClicked()
    {
        if (hasVoted) return;
        hasVoted = true;
        UpdateStatusText("Returning to main menu...");
        DisableButtons();
        SendVote(false);
    }

    private void SendVote(bool wantsToContinue)
    {
        if (networking == null)
        {
            Debug.LogError("[LevelTransitionUI] Networking is null!");
            return;
        }

        if (networking.mode != Networking.NetworkMode.Client)
        {
            Debug.LogError($"[LevelTransitionUI] Wrong networking mode: {networking.mode}. Need Client mode!");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            int playerID = gameManager.localPlayerID;
            Debug.Log($"[LevelTransitionUI] Sending vote for Player {playerID}: {(wantsToContinue ? "Continue" : "Return")}");
            
            LevelTransitionMessage msg = new LevelTransitionMessage(playerID, wantsToContinue);
            byte[] data = NetworkSerializer.Serialize(msg);

            if (data != null)
            {
                networking.SendBytes(data);
                Debug.Log($"[LevelTransitionUI] Vote sent successfully");
            }
            else
            {
                Debug.LogError("[LevelTransitionUI] Failed to serialize vote!");
            }
        }
        else
        {
            Debug.LogError("[LevelTransitionUI] GameManager is null!");
        }
    }

    private void EnableButtons()
    {
        if (continueButton != null)
            continueButton.interactable = true;
        if (returnButton != null)
            returnButton.interactable = true;
    }

    private void DisableButtons()
    {
        if (continueButton != null)
            continueButton.interactable = false;

        if (returnButton != null)
            returnButton.interactable = false;
    }

    private void UpdateStatusText(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void OnDestroy()
    {
        if (Time.timeScale == 0f)
            Time.timeScale = previousTimeScale;
    }
}