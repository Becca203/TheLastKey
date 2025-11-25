using UnityEngine;
using System.Collections.Generic;

public class NetworkIDManager : MonoBehaviour
{
    private static NetworkIDManager instance;
    public static NetworkIDManager Instance => instance;
    private Dictionary<int, GameObject> idToObject = new Dictionary<int, GameObject>();
    private Dictionary<GameObject, int> objectToID = new Dictionary<GameObject, int>();
    private int nextID = 1;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public int RegisterObject(GameObject obj)
    {
        if (objectToID.ContainsKey(obj))
            return objectToID[obj];

        int newID = nextID++;
        idToObject.Add(newID, obj);
        objectToID.Add(obj, newID);

        Debug.Log($"[NetworkIDManager] Registered {obj.name} with ID: {newID}");
        return newID; 
    }
    public void UnregisterObject(GameObject obj)
    {
        if (objectToID.ContainsKey(obj))
        {
            int id = objectToID[obj];
            idToObject.Remove(id);
            objectToID.Remove(obj);
        }
    }

    public GameObject GetObjectByID(int id)
    {
        return idToObject.ContainsKey(id) ? idToObject[id] : null;
    }
    public int GetIDForObject(GameObject obj)
    {
        return objectToID.ContainsKey(obj) ? objectToID[obj] : -1;
    }

}
