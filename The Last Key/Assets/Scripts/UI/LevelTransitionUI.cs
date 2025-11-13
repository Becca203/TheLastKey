using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelTransitionUI : MonoBehaviour
{
    public GameObject panel;
    public TextMeshProUGUI statusText;
    public Button continueButton;
    public Button returnButton;

    private UDPClient udpClient;
    private bool hasVoted = false;

    private void Start()
    {
        if (panel != null) 
            panel.SetActive(false);
        if (continueButton != null) 
            continueButton.onClick.AddListener(OnContinueClicked);
        if (returnButton != null) 
            returnButton.onClick.AddListener(OnReturnClicked);

        udpClient = FindAnyObjectByType<UDPClient>();
    }

    public void ShowPanel()
    {
        if (panel != null)
        {
            panel.SetActive(true);
            hasVoted = false;
            UpdateStatusText("Waiting for your decision...");
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
        if (udpClient == null) return;

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            int playerID = gameManager.localPlayerID;
            LevelTransitionMessage msg = new LevelTransitionMessage(playerID, wantsToContinue);
            byte[] data = NetworkSerializer.Serialize(msg);

            if (data != null)
            {
                udpClient.SendBytes(data);
                Debug.Log($"Player {playerID} voted: {(wantsToContinue ? "Continue" : "Return")}");
            }
        }
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
}
