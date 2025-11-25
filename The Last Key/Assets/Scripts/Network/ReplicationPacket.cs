using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ReplicationPacket : NetworkMessage
{
    public List<ReplicationCommand> commands = new List<ReplicationCommand>();
    public long timestamp;

    public ReplicationPacket() : base("REPLICATION")
    {
        timestamp = System.DateTime.Now.Ticks;
    }

    public void AddCommand(ReplicationCommand command)
    {
        commands.Add(command);
    }
}
