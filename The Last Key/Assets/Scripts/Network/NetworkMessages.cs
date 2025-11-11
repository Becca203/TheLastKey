using System;
using System.Collections.Generic;

[Serializable]
public class NetworkMessage
{
    public string messageType;

    public NetworkMessage(string type)
    {
        messageType = type;
    }
}

[Serializable]
public class PositionMessage : NetworkMessage
{
    public int playerID;
    public float posX;
    public float posY;
    public float velX;
    public float velY;
    public long timestamp;

    public PositionMessage() : base("POSITION") { }

    public PositionMessage(int id, float x, float y, float vx, float vy) : base("POSITION")
    {
        playerID = id;
        posX = x;
        posY = y;
        velX = vx;
        velY = vy;
        timestamp = DateTime.Now.Ticks;
    }
}

[Serializable]
public class ChatMessage : NetworkMessage
{
    public string username;
    public string message;
    public long timestamp;

    public ChatMessage() : base("CHAT") { }

    public ChatMessage(string user, string msg) : base("CHAT")
    {
        username = user;
        message = msg;
        timestamp = DateTime.Now.Ticks;
    }
}

[Serializable]
public class PlayerListMessage : NetworkMessage
{
    public List<string> players = new List<string>();

    public PlayerListMessage() : base("PLAYER_LIST") { }
}

[Serializable]
public class GameStartMessage : NetworkMessage
{
    public int assignedPlayerID;
    public int totalPlayers;

    public GameStartMessage() : base("GAME_START") { }

    public GameStartMessage(int playerID, int total) : base("GAME_START")
    {
        assignedPlayerID = playerID;
        totalPlayers = total;
    }
}

[Serializable]
public class SimpleMessage : NetworkMessage
{
    public string content;

    public SimpleMessage(string type) : base(type) { }

    public SimpleMessage(string type, string data) : base(type)
    {
        content = data;
    }
}

[Serializable]
public class KeyTransferMessage : NetworkMessage
{
    public int fromPlayerID;
    public int toPlayerID;

    public KeyTransferMessage() : base("KEY_TRANSFER") { }

    public KeyTransferMessage(int from, int to) : base("KEY_TRANSFER")
    {
        fromPlayerID = from;
        toPlayerID = to;
    }
}
// ELIMINA TODA LA CLASE PushMessage de aquí