using UnityEngine;

[System.Serializable]
public class ReplicationCommand
{
    public int networkID;
    public string action; // CREATE, UPDATE, DESTROY, EVENT
    public string objectType;

    // Replicated data
    public float posX;
    public float posY;
    public float velX;
    public float velY;
    public bool hasKey;
    public string state;

    public ReplicationCommand(int id, string actionType, string objType)
    {
        networkID = id;
        action = actionType;
        objectType = objType;
    }

    public void SetPosition(Vector3 position)
    {
        posX = position.x;
        posY = position.y;
    }

    public void SetVelocity(Vector2 velocity)
    {
        velX = velocity.x;
        velY = velocity.y;
    }

    public Vector3 GetPosition()
    {
        return new Vector3(posX, posY, 0);
    }

    public Vector2 GetVelocity()
    {
        return new Vector2(velX, velY);
    }
}
