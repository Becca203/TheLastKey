using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        public int playerID; // NUEVO

        public ClientInfo(IPEndPoint ep, int id)
        {
            endpoint = ep;
            username = "";
            playerID = id; // Asignar ID al crear
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    private void Start()
    {
        CreateAndBindTheSocket();
        Thread receiveThread = new Thread(ReceiveMessages);
        receiveThread.Start();
    }

    private void CreateAndBindTheSocket()
    {
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        ipep = new IPEndPoint(IPAddress.Any, 9050);
        serverSocket.Bind(ipep);
        Debug.Log("UDP Server started on port 9050, waiting for clients...");
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[1024];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receiveBytes = serverSocket.ReceiveFrom(buffer, ref remoteEndPoint);
                string message = Encoding.ASCII.GetString(buffer, 0, receiveBytes);
                IPEndPoint clientEndPoint = (IPEndPoint)remoteEndPoint;

                // Filtrar mensajes POSITION del log para no saturar
                if (!message.StartsWith("POSITION:"))
                {
                    Debug.Log("Received from client: " + message);
                }

                ClientInfo client = GetOrCreateClient(clientEndPoint);

                if (message.StartsWith("USERNAME:"))
                {
                    string username = message.Substring(9).Trim();
                    if (client.username != username)
                    {
                        client.username = username;
                        Debug.Log($"User joined: {username} as Player {client.playerID}");
                        SendServerName(clientEndPoint);
                        SendUserList(client);
                        BroadcastToClients("PLAYER_JOINED:" + username, client);
                        CheckAndStartGame();
                    }
                }
                else if (message.StartsWith("CHAT:"))
                {
                    string chatMessage = message.Substring(5);
                    Debug.Log("From " + client.username + ": " + chatMessage);
                    BroadcastToClients("CHAT:" + client.username + ":" + chatMessage);
                }
                // NUEVO: Reenviar actualizaciones de posición al otro jugador
                else if (message.StartsWith("POSITION:"))
                {
                    ForwardPositionUpdate(message, client);
                }

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

    // NUEVO: Reenviar posición solo al otro jugador
    private void ForwardPositionUpdate(string message, ClientInfo sender)
    {
        byte[] data = Encoding.ASCII.GetBytes(message);
        lock (clientsLock)
        {
            foreach (var client in connectedClients)
            {
                // Enviar solo al otro jugador (no al que envió)
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

    // MODIFICADO: Enviar ID de jugador con GAME_START
    private void CheckAndStartGame()
    {
        lock (clientsLock)
        {
            int connectedPlayerCount = connectedClients.Count;
            if (connectedPlayerCount == 2 && !gameStarted)
            {
                gameStarted = true;
                Debug.Log("Starting game...");

                // Enviar a cada cliente su ID específico
                foreach (var client in connectedClients)
                {
                    if (!string.IsNullOrEmpty(client.username))
                    {
                        string startMessage = "GAME_START:" + client.playerID;
                        byte[] data = Encoding.ASCII.GetBytes(startMessage);
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
                BroadcastToClients("GAME_CANCEL:");
            }
        }
    }

    private void HandlePlayerDisconnection(ClientInfo disconnectedClient)
    {
        lock (clientsLock)
        {
            if (connectedClients.Remove(disconnectedClient))
            {
                Debug.Log("Player disconnected: " + disconnectedClient.username);
                if (!string.IsNullOrEmpty(disconnectedClient.username))
                {
                    BroadcastToClients("PLAYER_LEFT:" + disconnectedClient.username);
                    CheckAndStartGame();
                }
            }
        }
    }

    private void BroadcastToClients(string message, ClientInfo excludedClient = null)
    {
        byte[] data = Encoding.ASCII.GetBytes(message);
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

        if (!message.StartsWith("POSITION:"))
        {
            Debug.Log("Broadcasted message: " + message);
        }
    }

    private void SendUserList(ClientInfo newClient)
    {
        lock (clientsLock)
        {
            string playerList = "PLAYER_LIST:";
            foreach (var c in connectedClients)
            {
                if (!string.IsNullOrEmpty(c.username))
                {
                    playerList += c.username + ",";
                }
            }
            if (playerList.EndsWith(","))
                playerList = playerList.Substring(0, playerList.Length - 1);

            byte[] data = Encoding.ASCII.GetBytes(playerList);
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

    // MODIFICADO: Asignar ID automáticamente
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

            // Asignar ID basado en el número de clientes (1 o 2)
            int newPlayerID = connectedClients.Count + 1;
            ClientInfo newClient = new ClientInfo(clientEndPoint, newPlayerID);
            connectedClients.Add(newClient);
            Debug.Log("New client registered: " + clientEndPoint + " as Player " + newPlayerID);
            return newClient;
        }
    }

    private void SendServerName(IPEndPoint endPoint)
    {
        string message = "SERVER_NAME:" + serverName;
        byte[] data = Encoding.ASCII.GetBytes(message);
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

    private void SendPing(IPEndPoint endPoint)
    {
        try
        {
            string ping = "ping";
            byte[] data = Encoding.ASCII.GetBytes(ping);
            serverSocket.SendTo(data, endPoint);
        }
        catch (Exception e)
        {
            Debug.LogError("Error sending ping: " + e.Message);
        }
    }

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