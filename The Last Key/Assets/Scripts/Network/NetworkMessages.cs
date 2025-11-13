using System;
using System.Collections.Generic;
using UnityEngine;

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

[Serializable]
public class PushMessage : NetworkMessage
{
    public int pushedPlayerID;
    public float velocityX;
    public float velocityY;
    public float duration;

    public PushMessage() : base("PUSH") { }

    public PushMessage(int playerID, Vector2 velocity, float dur) : base("PUSH")
    {
        pushedPlayerID = playerID;
        velocityX = velocity.x;
        velocityY = velocity.y;
        duration = dur;
    }
}

[Serializable]
public class LevelTransitionMessage : NetworkMessage
{
    public int playerID;
    public bool wantsToContinue;

    public LevelTransitionMessage() : base("LEVEL_TRANSITION") { }

    public LevelTransitionMessage(int id, bool continues) : base("LEVEL_TRANSITION")
    {
        playerID = id;
        wantsToContinue = continues;
    }
}

[Serializable]
public class LoadSceneMessage : NetworkMessage
{
    public string sceneName;

    public LoadSceneMessage() : base("LOAD_SCENE") { }

    public LoadSceneMessage(string scene) : base("LOAD_SCENE")
    {
        sceneName = scene;
    }
}

[Serializable]
public class LevelCompleteMessage : NetworkMessage
{
    public string nextLevelName;

    public LevelCompleteMessage() : base("LEVEL_COMPLETE") { }

    public LevelCompleteMessage(string nextLevel) : base("LEVEL_COMPLETE")
    {
        nextLevelName = nextLevel;
    }
}


