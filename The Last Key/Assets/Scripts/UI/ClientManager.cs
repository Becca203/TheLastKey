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
        // Validate that both IP and username fields are filled
        if (string.IsNullOrEmpty(ipInputField.text) || string.IsNullOrEmpty(usernameInputField.text))
        {
            statusText.text = "Please enter IP and username";
            return;// Exit early if validation fails
        }

        // Create a new GameObject to host the UDPClient component
        GameObject clientObject = new GameObject("UDPClient");
        DontDestroyOnLoad(clientObject);

        // Add and configure the UDPClient component
        var udpClient = clientObject.AddComponent<UDPClient>();
        udpClient.serverIP = ipInputField.text.Trim();
        udpClient.username = usernameInputField.text.Trim();

        // Inform the user that connection is being attempted
        statusText.text = "Connecting to server...";
    }
}
