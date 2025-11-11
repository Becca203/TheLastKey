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
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        ShowAvailableIPs();
        CreateAndBindTheSocket();
    }

    private void ShowAvailableIPs()
    {
        string hostName = Dns.GetHostName();
        IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

        Debug.Log("===== SERVER AVAILABLE IPs =====");
        foreach (IPAddress ip in hostEntry.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                Debug.Log($"IPv4: {ip}");
            }
        }
        Debug.Log("================================");
    }

    private void CreateAndBindTheSocket()
    {
        try
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            ipep = new IPEndPoint(IPAddress.Any, 9050);
            serverSocket.Bind(ipep);

            Thread receiveThread = new Thread(ReceiveMessages)
            {
                IsBackground = true,
                Name = "UDP_Server_Receive"
            };
            receiveThread.Start();

            Debug.Log($"[SERVER] Started on port {ipep.Port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SERVER] Error creating socket: {e.Message}");
        }
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = serverSocket.ReceiveFrom(buffer, ref remoteEndPoint);
                string msgType = NetworkSerializer.GetMessageType(buffer, receivedBytes);

                if (msgType != "POSITION")
                {
                    Debug.Log($"[SERVER] <<< Received {msgType} ({receivedBytes} bytes) from {remoteEndPoint}");
                }

                ClientInfo client = GetOrCreateClient((IPEndPoint)remoteEndPoint);
                ProcessMessage(msgType, buffer, receivedBytes, client);
            }
            catch (SocketException se)
            {
                if (isRunning)
                {
                    Debug.LogError($"[SERVER] Socket error: {se.ErrorCode} - {se.Message}");
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (Exception e)
            {
                if (isRunning)
                {
                    Debug.LogError($"[SERVER] Receive error: {e.Message}");
                }
            }
        }
    }

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
            case "START_GAME":
                ProcessStartGameRequest();
                break;
            case "GAME_OVER":
                ProcessGameOverMessage(buffer, length);
                break;
            case "KEY_COLLECTED":
                ProcessKeyCollectedMessage(buffer, length, client);
                break;
            case "KEY_TRANSFER":
                ProcessKeyTransferMessage(buffer, length);
                break;
            default:
                Debug.LogWarning("Unknown message type: " + msgType);
                break;
        }
    }

    private ClientInfo GetOrCreateClient(IPEndPoint endpoint)
    {
        lock (clientsLock)
        {
            foreach (ClientInfo client in connectedClients)
            {
                if (client.endpoint.Equals(endpoint))
                {
                    return client;
                }
            }

            ClientInfo newClient = new ClientInfo
            {
                endpoint = endpoint,
                username = "",
                playerID = connectedClients.Count + 1
            };
            connectedClients.Add(newClient);

            Debug.Log($"[SERVER] New client added: {endpoint} as Player {newClient.playerID}");
            return newClient;
        }
    }

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
            }
        }
    }

    private void SendServerName(IPEndPoint clientEndpoint)
    {
        try
        {
            SimpleMessage msg = new SimpleMessage("USERNAME", "SERVER_NAME:" + serverName);
            byte[] data = NetworkSerializer.Serialize(msg);
            serverSocket.SendTo(data, clientEndpoint);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SERVER] Error sending server name: {e.Message}");
        }
    }

    private void SendUserList()
    {
        lock (clientsLock)
        {
            PlayerListMessage playerListMsg = new PlayerListMessage();
            foreach (ClientInfo client in connectedClients)
            {
                if (!string.IsNullOrEmpty(client.username))
                {
                    playerListMsg.players.Add(client.username);
                }
            }

            byte[] data = NetworkSerializer.Serialize(playerListMsg);
            foreach (ClientInfo client in connectedClients)
            {
                try
                {
                    serverSocket.SendTo(data, client.endpoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SERVER] Error sending user list to {client.username}: {e.Message}");
                }
            }
        }
    }

    private void ProcessChatMessage(byte[] buffer, int length)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
            Debug.Log($"[SERVER] Chat from {chatMsg.username}: {chatMsg.message}");
            BroadcastMessage(chatMsg);
        }
    }

    private void ProcessPositionMessage(byte[] buffer, int length, ClientInfo sender)
    {
        if (!gameStarted) return;

        lock (clientsLock)
        {
            foreach (ClientInfo client in connectedClients)
            {
                if (client != sender)
                {
                    try
                    {
                        serverSocket.SendTo(buffer, length, SocketFlags.None, client.endpoint);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[SERVER] Error forwarding position to {client.username}: {e.Message}");
                    }
                }
            }
        }
    }

    private void ProcessStartGameRequest()
    {
        lock (clientsLock)
        {
            if (connectedClients.Count >= 2)
            {
                Debug.Log("Starting game for all clients...");
                gameStarted = true;

                int playerID = 1;
                foreach (var client in connectedClients)
                {
                    if (!string.IsNullOrEmpty(client.username))
                    {
                        GameStartMessage gameStartMsg = new GameStartMessage(playerID, connectedClients.Count);
                        byte[] data = NetworkSerializer.Serialize(gameStartMsg);

                        try
                        {
                            serverSocket.SendTo(data, client.endpoint);
                            Debug.Log("Sent GAME_START to " + client.username + " as Player " + playerID);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Error sending game start to " + client.username + ": " + e.Message);
                        }
                        playerID++;
                    }
                }
            }
            else
            {
                Debug.LogWarning("Not enough players to start the game");
            }
        }
    }

    private void ProcessGameOverMessage(byte[] buffer, int length)
    {
        SimpleMessage gameOverMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (gameOverMsg != null)
        {
            Debug.Log("Server received GAME_OVER - Winner: Player " + gameOverMsg.content);
            BroadcastMessage(gameOverMsg);
            gameStarted = false;
        }
    }

    private void ProcessKeyCollectedMessage(byte[] buffer, int length, ClientInfo client)
    {
        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null)
        {
            Debug.Log("Server received KEY_COLLECTED from Player " + keyMsg.content);

            SimpleMessage updateMsg = new SimpleMessage("UPDATE_KEY_STATE", keyMsg.content);
            BroadcastMessage(updateMsg);

            SimpleMessage hideMsg = new SimpleMessage("HIDE_KEY", "");
            BroadcastMessage(hideMsg);
        }
    }

    private void ProcessKeyTransferMessage(byte[] buffer, int length)
    {
        KeyTransferMessage transferMsg = NetworkSerializer.Deserialize<KeyTransferMessage>(buffer, length);
        if (transferMsg != null)
        {
            Debug.Log("[SERVER] Key transferred from Player " + transferMsg.fromPlayerID + " to Player " + transferMsg.toPlayerID);
            BroadcastMessage(transferMsg);
        }
    }

    private void BroadcastMessage(NetworkMessage message, ClientInfo exclude = null)
    {
        byte[] data = NetworkSerializer.Serialize(message);
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (ClientInfo client in connectedClients)
            {
                if (exclude != null && client == exclude) continue;

                try
                {
                    serverSocket.SendTo(data, client.endpoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SERVER] Error broadcasting to {client.username}: {e.Message}");
                }
            }
        }
    }

    void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;
        isRunning = false;

        Debug.Log("[SERVER] Shutting down...");
        try
        {
            if (serverSocket != null)
            {
                serverSocket.Close();
            }
        }
        catch { }
    }

    void OnApplicationQuit() => Shutdown();
    void OnDestroy() => Shutdown();
}