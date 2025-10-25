using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class UDPServer : MonoBehaviour
{
    Socket serverSocket;
    IPEndPoint ipep;
    private bool isRunning = true;
    private bool hasShutdown = false;
    private string serverName = "GameServer";
    private List<ClientInfo> connectedClients = new List<ClientInfo>();
    private object clientsLock = new object();
    private bool gameStarted = false;

    private class ClientInfo
    {
        public IPEndPoint endpoint;
        public string username;
        public int playerID;

        public ClientInfo(IPEndPoint ep, int id)
        {
            endpoint = ep;
            username = "";
            playerID = id;
        }
    }

    // Ensure server persists across scenes
    private void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    // Initialize server socket and start listening
    private void Start()
    {
        CreateAndBindTheSocket();
        Thread receiveThread = new Thread(ReceiveMessages);
        receiveThread.Start();
    }

    // Create and bind UDP socket to port
    private void CreateAndBindTheSocket()
    {
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        ipep = new IPEndPoint(IPAddress.Any, 9050);
        serverSocket.Bind(ipep);
        Debug.Log("UDP Server started on port 9050");
    }

    // Continuously receive and process messages from clients
    void ReceiveMessages()
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receiveBytes = serverSocket.ReceiveFrom(buffer, ref remoteEndPoint);
                IPEndPoint clientEndPoint = (IPEndPoint)remoteEndPoint;

                string msgType = NetworkSerializer.GetMessageType(buffer, receiveBytes);

                if (msgType != "POSITION")
                {
                    Debug.Log("Server received: " + msgType);
                }

                ClientInfo client = GetOrCreateClient(clientEndPoint);
                ProcessMessage(msgType, buffer, receiveBytes, client);
                SendPing(clientEndPoint);
            }
            catch (SocketException se)
            {
                if (isRunning)
                {
                    Debug.Log("Socket error: " + se.Message);
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogError("Error receiving: " + e.Message);
                break;
            }
        }
    }

    // Route message to appropriate handler based on type
    private void ProcessMessage(string msgType, byte[] buffer, int length, ClientInfo client)
    {
        switch (msgType)
        {
            case "USERNAME":
                ProcessUsernameMessage(buffer, length, client);
                break;
            case "CHAT":
                ProcessChatMessage(buffer, length);
                break;
            case "POSITION":
                ProcessPositionMessage(buffer, length, client);
                break;
            default:
                Debug.LogWarning("Unknown message type: " + msgType);
                break;
        }
    }

    // Handle new client username and notify other clients
    private void ProcessUsernameMessage(byte[] buffer, int length, ClientInfo client)
    {
        SimpleMessage usernameMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (usernameMsg != null)
        {
            if (client.username != usernameMsg.content)
            {
                client.username = usernameMsg.content;
                Debug.Log("User joined: " + usernameMsg.content + " as Player " + client.playerID);

                SendServerName(client.endpoint);
                SendUserList();

                SimpleMessage joinedMsg = new SimpleMessage("PLAYER_JOINED", usernameMsg.content);
                BroadcastMessage(joinedMsg, client);

                CheckAndStartGame();
            }
        }
    }

    // Broadcast chat message to all clients
    private void ProcessChatMessage(byte[] buffer, int length)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
            Debug.Log("Chat from " + chatMsg.username + ": " + chatMsg.message);
            BroadcastMessage(chatMsg);
        }
    }

    // Forward position update to other clients
    private void ProcessPositionMessage(byte[] buffer, int length, ClientInfo sender)
    {
        PositionMessage posMsg = NetworkSerializer.Deserialize<PositionMessage>(buffer, length);
        if (posMsg != null)
        {
            ForwardPositionUpdate(posMsg, sender);
        }
    }

    // Send position update to all clients except sender
    private void ForwardPositionUpdate(PositionMessage posMsg, ClientInfo sender)
    {
        byte[] data = NetworkSerializer.Serialize(posMsg);
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (var client in connectedClients)
            {
                if (client != sender && !string.IsNullOrEmpty(client.username))
                {
                    try
                    {
                        serverSocket.SendTo(data, client.endpoint);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error forwarding position to " + client.username + ": " + e.Message);
                    }
                }
            }
        }
    }

    // Check if game should start or cancel based on player count
    private void CheckAndStartGame()
    {
        lock (clientsLock)
        {
            int connectedPlayerCount = connectedClients.Count;
            if (connectedPlayerCount == 2 && !gameStarted)
            {
                gameStarted = true;
                Debug.Log("Starting game with 2 players");

                foreach (var client in connectedClients)
                {
                    if (!string.IsNullOrEmpty(client.username))
                    {
                        GameStartMessage startMsg = new GameStartMessage(client.playerID, 2);
                        byte[] data = NetworkSerializer.Serialize(startMsg);

                        try
                        {
                            serverSocket.SendTo(data, client.endpoint);
                            Debug.Log("Sent GAME_START to " + client.username + " with ID " + client.playerID);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Error sending game start to " + client.username + ": " + e.Message);
                        }
                    }
                }
            }
            else if (connectedPlayerCount < 2 && gameStarted)
            {
                gameStarted = false;
                SimpleMessage cancelMsg = new SimpleMessage("GAME_CANCEL");
                BroadcastMessage(cancelMsg);
            }
        }
    }

    // Remove client and notify others of disconnection
    private void HandlePlayerDisconnection(ClientInfo disconnectedClient)
    {
        lock (clientsLock)
        {
            if (connectedClients.Remove(disconnectedClient))
            {
                Debug.Log("Player disconnected: " + disconnectedClient.username);
                if (!string.IsNullOrEmpty(disconnectedClient.username))
                {
                    SimpleMessage leftMsg = new SimpleMessage("PLAYER_LEFT", disconnectedClient.username);
                    BroadcastMessage(leftMsg);
                    CheckAndStartGame();
                }
            }
        }
    }

    // Send message to all connected clients except excluded one
    private void BroadcastMessage<T>(T message, ClientInfo excludedClient = null) where T : NetworkMessage
    {
        byte[] data = NetworkSerializer.Serialize(message);
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (var client in connectedClients)
            {
                if (client == excludedClient || string.IsNullOrEmpty(client.username))
                    continue;

                try
                {
                    serverSocket.SendTo(data, client.endpoint);
                }
                catch (Exception e)
                {
                    Debug.LogError("Error broadcasting to " + client.username + ": " + e.Message);
                    HandlePlayerDisconnection(client);
                }
            }
        }

        if (message.messageType != "POSITION")
        {
            Debug.Log("Broadcasted message: " + message.messageType);
        }
    }

    // Send current player list to all clients
    private void SendUserList()
    {
        lock (clientsLock)
        {
            PlayerListMessage playerListMsg = new PlayerListMessage();
            foreach (var c in connectedClients)
            {
                if (!string.IsNullOrEmpty(c.username))
                {
                    playerListMsg.players.Add(c.username);
                }
            }

            byte[] data = NetworkSerializer.Serialize(playerListMsg);
            foreach (var client in connectedClients)
            {
                try
                {
                    if (!string.IsNullOrEmpty(client.username))
                    {
                        serverSocket.SendTo(data, client.endpoint);
                        Debug.Log("Sent player list to: " + client.username);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error sending player list: " + e.Message);
                    HandlePlayerDisconnection(client);
                }
            }
        }
    }

    // Find existing client or create new one
    private ClientInfo GetOrCreateClient(IPEndPoint clientEndPoint)
    {
        lock (clientsLock)
        {
            foreach (var client in connectedClients)
            {
                if (client.endpoint.Address.Equals(clientEndPoint.Address) &&
                    client.endpoint.Port == clientEndPoint.Port)
                {
                    return client;
                }
            }

            int newPlayerID = connectedClients.Count + 1;
            ClientInfo newClient = new ClientInfo(clientEndPoint, newPlayerID);
            connectedClients.Add(newClient);
            Debug.Log("New client registered: " + clientEndPoint + " as Player " + newPlayerID);
            return newClient;
        }
    }

    // Send server name to newly connected client
    private void SendServerName(IPEndPoint endPoint)
    {
        SimpleMessage msg = new SimpleMessage("USERNAME", "SERVER_NAME:" + serverName);
        byte[] data = NetworkSerializer.Serialize(msg);
        try
        {
            serverSocket.SendTo(data, endPoint);
            Debug.Log("Sent server name to client: " + serverName);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending server name: " + e.Message);
        }
    }

    // Send ping to keep connection alive
    private void SendPing(IPEndPoint endPoint)
    {
        try
        {
            SimpleMessage ping = new SimpleMessage("ping");
            byte[] data = NetworkSerializer.Serialize(ping);
            serverSocket.SendTo(data, endPoint);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending ping: " + e.Message);
        }
    }

    // Close socket and cleanup
    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;
        try
        {
            if (serverSocket != null) serverSocket.Close();
        }
        catch { }
    }

    void OnApplicationQuit() => Shutdown();
    void OnDestroy() => Shutdown();
}