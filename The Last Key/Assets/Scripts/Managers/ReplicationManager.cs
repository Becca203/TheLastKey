using UnityEngine;
using System.Collections.Generic;
using System;

public class ReplicationManager : MonoBehaviour
{
    private UDPServer udpServer;
    private Dictionary<int, ReplicationCommand> pendingActions = new Dictionary<int, ReplicationCommand>();
    private float lastReplicationTime = 0f;
    private float replicationInterval = 0.1f;

    private void Start()
    {
        udpServer = GetComponent<UDPServer>();
    }

    public void QueueAction(int networkID, string action, GameObject obj = null)
    {
        string objectType = obj != null ? obj.name : "Unknown";
        ReplicationCommand command = new ReplicationCommand(networkID, action, objectType);

        if (obj != null)
        {
            command.SetPosition(obj.transform.position);

            NetworkPlayer networkPlayer = obj.GetComponent<NetworkPlayer>();
            if (networkPlayer != null)
            {
                command.hasKey = networkPlayer.hasKey;
                command.state = networkPlayer.isPushed ? "Pushed" : "Normal";

                Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    command.SetVelocity(rb.linearVelocity);
                }
            }
        }

        if (pendingActions.ContainsKey(networkID))
        {
            pendingActions[networkID] = command;
        }
        else
        {
            pendingActions.Add(networkID, command);
        }

    }

    public void ProcessReplication()
    {
        if (pendingActions.Count == 0) return;

        ReplicationPacket packet = new ReplicationPacket();

        foreach (var action in pendingActions.Values)
        {
            packet.AddCommand(action);
        }

        SendReplicationPacket(packet);
        pendingActions.Clear();
        lastReplicationTime = Time.time;
        Debug.Log($"[ReplicationManager] Sent replication packet with {packet.commands.Count} commands");
    }

    private void SendReplicationPacket(ReplicationPacket packet)
    {
        byte[] data = NetworkSerializer.Serialize(packet);
        if (data == null) return;

        var clientsField = typeof(UDPServer).GetField("connectedClients",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (clientsField != null)
        {
            List<UDPServer.ClientInfo> connectedClients = clientsField.GetValue(udpServer) as List<UDPServer.ClientInfo>;

            if (connectedClients != null)
            {
                foreach (var client in connectedClients)
                {
                    try
                    {
                        udpServer.SendToClient(data, client.endpoint);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[ReplicationManager] Error sending to client: {e.Message}");
                    }
                }
            }
        }
    }
    void Update()
    {
        if (Time.time - lastReplicationTime >= replicationInterval)
            ProcessReplication();
    }

    public void SendToClient(byte[] data, System.Net.IPEndPoint endpoint)
    {
        if (udpServer != null)
        {
            var socketField = typeof(UDPServer).GetField("serverSocket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (socketField != null)
            {
                Socket socket = socketField.GetValue(udpServer) as Socket;
                socket?.SendTo(data, endpoint);
            }
        }
    }
}
