using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientManager : MonoBehaviour
{
    public TMP_InputField ipInputField;
    public TMP_InputField usernameInputField;
    public Button playButton;
    public TextMeshProUGUI statusText;

    void Start()
    {
        playButton.onClick.AddListener(ConnectToServer);
        statusText.text = "Enter server IP and username to connect.";
    }

    // Validates user input and creates a UDP client to connect to the server.
    private void ConnectToServer()
    {
        if (string.IsNullOrEmpty(ipInputField.text) || string.IsNullOrEmpty(usernameInputField.text))
        {
            statusText.text = "Please enter IP and username";
            return;
        }

        GameObject clientObject = new GameObject("UDPClient");
        DontDestroyOnLoad(clientObject);

        var udpClient = clientObject.AddComponent<UDPClient>();
        udpClient.serverIP = ipInputField.text.Trim();
        udpClient.username = usernameInputField.text.Trim();

        statusText.text = "Connecting to server...";
    }
}
