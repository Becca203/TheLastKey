using System;
using System.Text;
using UnityEngine;

public static class NetworkSerializer
{
    // Converts a NetworkMessage object to bytes using JSON serialization
    public static byte[] Serialize<T>(T message) where T : NetworkMessage
    {
        try
        {
            string json = JsonUtility.ToJson(message);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception e)
        {
            Debug.LogError("Error serializing " + typeof(T).Name + ": " + e.Message);
            return null;
        }
    }

    // Converts received bytes back into a NetworkMessage object using JSON deserialization
    public static T Deserialize<T>(byte[] data, int length) where T : NetworkMessage
    {
        try
        {
            string json = Encoding.UTF8.GetString(data, 0, length);
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("Error deserializing to " + typeof(T).Name + ": " + e.Message);
            return null;
        }
    }

    // Extracts the message type from received bytes without full deserialization
    public static string GetMessageType(byte[] data, int length)
    {
        try
        {
            string json = Encoding.UTF8.GetString(data, 0, length);
            NetworkMessage baseMsg = JsonUtility.FromJson<NetworkMessage>(json);
            return baseMsg?.messageType ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }

    // Serialization methods for ReliablePacket
    public static byte[] SerializeReliable(ReliablePacket packet)
    {
        try
        {
            string json = JsonUtility.ToJson(packet);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception e)
        {
            Debug.LogError("Error serializing ReliablePacket: " + e.Message);
            return null;
        }
    }

    public static ReliablePacket DeserializeReliable(byte[] data, int length)
    {
        try
        {
            string json = Encoding.UTF8.GetString(data, 0, length);
            return JsonUtility.FromJson<ReliablePacket>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("Error deserializing ReliablePacket: " + e.Message);
            return null;
        }
    }
}