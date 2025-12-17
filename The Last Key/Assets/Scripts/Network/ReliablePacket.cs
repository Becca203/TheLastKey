using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ReliablePacket
{
    public uint sequenceNumber;      // To detect duplicates 
    public uint ackSequence;         // ACK of the last received packet
    public bool isAck;               // This packet is only an ACK
    public bool needsAck;            // This packet requires confirmation
    public string messageType;       // Original message type (POSITION, KEY_COLLECT, etc)
    public byte[] payload;           // Original message data
    public long timestamp;           // For latency detection
    public ReliablePacket()
    {
        sequenceNumber = 0;
        ackSequence = 0;
        isAck = false;
        needsAck = false;
        messageType = "";
        payload = null;
        timestamp = System.DateTime.Now.Ticks;
    }
}

// Manages reliable packet sending and receiving
public class ReliabilityManager
{
    private uint nextSequenceNumber = 1;
    private uint lastReceivedSequence = 0;
    private uint lastAckedSequence = 0;

    // Packages sent but not yet acknowledged
    private Dictionary<uint, (ReliablePacket packet, float sendTime)> 
    pendingPackets = new Dictionary<uint, (ReliablePacket, float)>();

    [SerializeField] private float ackTimeout = 3f;  // If no ACK in this time, retransmit
    [SerializeField] private float maxRetransmitTime = 5f;   
    [SerializeField] private int maxRetransmits = 2;        

    private Dictionary<uint, int> retransmitCounts = new Dictionary<uint, int>();

    public uint GetNextSequence() => nextSequenceNumber++;

    public void UpdateLastReceivedSequence(uint seq)
    {
        lastReceivedSequence = seq;
    }

    public uint GetLastReceivedSequence() => lastReceivedSequence;

    public void RegisterPendingPacket(ReliablePacket packet)
    {
        if (!packet.needsAck) return;
        
        pendingPackets[packet.sequenceNumber] = (packet, Time.time);
        retransmitCounts[packet.sequenceNumber] = 0;
    }

    public void AcknowledgePacket(uint sequenceNumber)
    {
        lastAckedSequence = sequenceNumber;
        pendingPackets.Remove(sequenceNumber);
        retransmitCounts.Remove(sequenceNumber);
    }

    // Returns list of packets that need retransmission
    public List<ReliablePacket> GetPacketsToRetransmit()
    {
        var toRetransmit = new List<ReliablePacket>();
        var toRemove = new List<uint>();
        float currentTime = Time.time;

        // Take a snapshot to avoid modifying while iterating
        var packetSnapshot = new Dictionary<uint, (ReliablePacket packet, float sendTime)>(pendingPackets);

        foreach (var kvp in packetSnapshot)
        {
            uint seq = kvp.Key;
            var (packet, sendTime) = kvp.Value;
            float elapsed = currentTime - sendTime;

            if (elapsed > maxRetransmitTime)
            {
                toRemove.Add(seq);
                continue;
            }

            if (retransmitCounts.ContainsKey(seq) && retransmitCounts[seq] >= maxRetransmits)
            {
                toRemove.Add(seq);
                continue;
            }

            if (elapsed > ackTimeout)
            {
                toRetransmit.Add(packet);
                if (!retransmitCounts.ContainsKey(seq))
                    retransmitCounts[seq] = 0;
                retransmitCounts[seq]++;
                pendingPackets[seq] = (packet, currentTime);
            }
        }

        foreach (uint seq in toRemove)
        {
            pendingPackets.Remove(seq);
            retransmitCounts.Remove(seq);
        }

        return toRetransmit;
    }

    public uint GetLastAckedSequence() => lastAckedSequence;

    public int GetPendingPacketCount() => pendingPackets.Count;

    public void ClearPendingPackets()
    {
        pendingPackets.Clear();
        retransmitCounts.Clear();
    }
}
