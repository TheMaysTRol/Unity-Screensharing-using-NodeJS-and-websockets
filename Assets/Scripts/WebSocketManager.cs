using System;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Events;

public class WebSocketManager : MonoBehaviour
{
    public string socketServer = "127.0.0.1";
    public string socketPort = "3000";


    private WebSocket _socket;
    private Dictionary<string, List<Action<JObject>>> resultsSub = new Dictionary<string, List<Action<JObject>>>();
    public UnityEvent OnSocketConnect;
    public UnityEvent<string> OnSocketDisconnect;
    public bool isSocketConnected = false;

    public static WebSocketManager instance;

    public string mySocketID = "";

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(this);
        }

        DontDestroyOnLoad(this);
    }

    void Start()
    {
        _socket = new WebSocket("ws://" + socketServer + ":" + socketPort);
        _socket.OnOpen += (sender, e) => OnSocketConnected(sender, e);
        _socket.OnMessage += (sender, e) => OnSocketRecieveMessage(e.Data);
        _socket.OnError += (sender, e) => OnSocketError(sender, e);
        _socket.OnClose += (sender, e) => OnSocketDisconnected(sender, e);

        On("GetId", (json) =>
        {
            mySocketID = json["data"]["id"].ToString();
        });


    }

    public void Connect(Action OnConnected)
    {
        _socket.OnOpen += (sender, e) => OnConnected.Invoke();
        _socket.ConnectAsync();
    }

    public void Disconnect()
    {
        _socket.CloseAsync();
    }


    private void OnSocketConnected(object sender, EventArgs e)
    {
        Debug.Log("socket.OnConnected");
        if (OnSocketConnect != null && OnSocketConnect.GetPersistentEventCount() > 0)
        {
            OnSocketConnect.Invoke();
        }
        isSocketConnected = true;

        //var mesg = new
        //{
        //    test = "test",
        //    bb = 55
        //};
        //SendSocketMessage("Message1", mesg);

    }

    private void OnSocketDisconnected(object sender, CloseEventArgs e)
    {

        if (OnSocketDisconnect != null && OnSocketDisconnect.GetPersistentEventCount() > 0)
        {
            OnSocketDisconnect.Invoke(e.Reason);
        }

        isSocketConnected = false;
        Debug.Log(e.Reason);
    }

    private void OnSocketError(object sender, ErrorEventArgs e)
    {
        Debug.Log(e.Message);
    }


    private void OnSocketRecieveMessage(string message)
    {
        JObject jsonObject = JObject.Parse(message);
        if (resultsSub.ContainsKey(jsonObject["id"].ToString()))
        {
            foreach (var callback in resultsSub[jsonObject["id"].ToString()])
            {
                callback.Invoke(jsonObject);
            }
        }
    }


    //stop listening to event
    public void Off(string key, Action<JObject> callbackToRemove)
    {
        if (!resultsSub.ContainsKey(key))
        {
            return; // Key doesn't exist, nothing to remove
        }

        resultsSub[key].RemoveAll(existingCallback => existingCallback == callbackToRemove);

        // If no callbacks are left for the key, remove the key itself
        if (resultsSub[key].Count == 0)
        {
            resultsSub.Remove(key);
        }
    }

    // Stop listening to all events for a key
    public void OffAll(string key)
    {
        if (resultsSub.ContainsKey(key))
        {
            resultsSub.Remove(key);
        }
    }

    //start listening to event
    public void On(string key, Action<JObject> callback)
    {
        if (!resultsSub.ContainsKey(key))
        {
            resultsSub.Add(key, new List<Action<JObject>>());
        }

        if (!resultsSub[key].Contains(callback))
        {
            resultsSub[key].Add(callback);
        }
    }

    public void CurrentSocketId()
    {

    }


    //Send message to server
    public void SendSocketMessage(string id, object jsonMessage, object extra = null)
    {
        if (!isSocketConnected)
        {
            return;
        }
        var mesg = new
        {
            id = id,
            data = jsonMessage,
            extradata = extra
        };

        string jsonString = JsonConvert.SerializeObject(mesg);
        //Debug.Log("sent : " + jsonString);
        _socket.SendAsync(jsonString, (b) =>
        {

        });
    }

    void OnDestroy()
    {
        _socket.Close();
    }
}

public class SocketResult
{
    public List<Action<JObject>> onGetResultList;

    public SocketResult(List<Action<JObject>> onGetResult)
    {
        this.onGetResultList = onGetResult;
    }
}
