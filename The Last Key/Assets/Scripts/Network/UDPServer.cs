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

    public string GetServerIP()
    {
        try
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SERVER] Error getting IP: {e.Message}");
        }

        return "127.0.0.1";
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
                Debug.LogWarning("[SERVER] Unknown message type: " + msgType);
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
                    Debug.LogError($"[SERVER] Error sending user list: {e.Message}");
                }
            }
        }
    }

    private void ProcessChatMessage(byte[] buffer, int length)
    {
        ChatMessage chatMsg = NetworkSerializer.Deserialize<ChatMessage>(buffer, length);
        if (chatMsg != null)
        {
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
                        Debug.LogError($"[SERVER] Error forwarding position: {e.Message}");
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
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[SERVER] Error sending game start: {e.Message}");
                        }
                        playerID++;
                    }
                }
            }
            else
            {
                Debug.LogWarning("[SERVER] Not enough players to start game");
            }
        }
    }

    private void ProcessGameOverMessage(byte[] buffer, int length)
    {
        SimpleMessage gameOverMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (gameOverMsg != null)
        {
            BroadcastMessage(gameOverMsg);
        }
    }

    private void ProcessKeyCollectedMessage(byte[] buffer, int length, ClientInfo client)
    {
        SimpleMessage keyMsg = NetworkSerializer.Deserialize<SimpleMessage>(buffer, length);
        if (keyMsg != null)
        {
            // Broadcast KEY_COLLECTED to all clients
            BroadcastMessage(keyMsg);
            
            // Send HIDE_KEY to hide the key object on all clients
            SimpleMessage hideKeyMsg = new SimpleMessage("HIDE_KEY", "");
            BroadcastMessage(hideKeyMsg);
        }
    }

    private void ProcessKeyTransferMessage(byte[] buffer, int length)
    {
        KeyTransferMessage transferMsg = NetworkSerializer.Deserialize<KeyTransferMessage>(buffer, length);
        if (transferMsg != null)
        {
            BroadcastMessage(transferMsg);
        }
    }

    private void BroadcastMessage<T>(T message, ClientInfo excludeClient = null) where T : NetworkMessage
    {
        byte[] data = NetworkSerializer.Serialize(message);
        if (data == null) return;

        lock (clientsLock)
        {
            foreach (ClientInfo client in connectedClients)
            {
                if (excludeClient != null && client.endpoint.Equals(excludeClient.endpoint))
                    continue;

                try
                {
                    serverSocket.SendTo(data, client.endpoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SERVER] Error broadcasting: {e.Message}");
                }
            }
        }
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (hasShutdown) return;
        hasShutdown = true;

        isRunning = false;

        if (serverSocket != null)
        {
            try
            {
                serverSocket.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SERVER] Error closing socket: {e.Message}");
            }
        }
    }
}