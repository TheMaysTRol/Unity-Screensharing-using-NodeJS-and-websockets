using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Events;

public class WebSocketManager : MonoBehaviour
{
    [Header("WebSocket Configuration")]
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
            DontDestroyOnLoad(this);
        }
        else
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        InitializeWebSocket();
        SubscribeToEvents(); // Now correctly included
    }

    private void InitializeWebSocket()
    {
        _socket = new WebSocket(WebSocketUrl);
        _socket.OnOpen += OnSocketConnected;
        _socket.OnMessage += OnSocketReceiveMessage;
        _socket.OnError += OnSocketError;
        _socket.OnClose += OnSocketDisconnected;
    }

    private string WebSocketUrl => $"ws://{socketServer}:{socketPort}";

    private void SubscribeToEvents()
    {
        // Subscribing to the "GetId" event to retrieve the socket ID
        On("GetId", (json) =>
        {
            mySocketID = json["data"]["id"].ToString();
        });
    }

    public void Connect(Action onConnected)
    {
        _socket.OnOpen += (sender, e) => onConnected?.Invoke();
        _socket.ConnectAsync();
    }

    public void Disconnect()
    {
        _socket.CloseAsync();
        isSocketConnected = false;
    }

    private void OnSocketConnected(object sender, EventArgs e)
    {
        Debug.Log("Socket connected.");
        OnSocketConnect?.Invoke();
        isSocketConnected = true;
    }

    private void OnSocketDisconnected(object sender, CloseEventArgs e)
    {
        OnSocketDisconnect?.Invoke(e.Reason);
        isSocketConnected = false;
        Debug.Log($"Socket disconnected: {e.Reason}");
    }

    private void OnSocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogError($"Socket error: {e.Message}");
    }

    private void OnSocketReceiveMessage(object sender, MessageEventArgs message)
    {
       // Debug.Log(message.Data);
        try
        {
            JObject jsonObject = JObject.Parse(message.Data);
            if (resultsSub.TryGetValue(jsonObject["id"].ToString(), out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback.Invoke(jsonObject);
                }
            }
        }
        catch (JsonException ex)
        {
            Debug.LogError($"JSON Parsing Error: {ex.Message}");
        }
    }

    public void Off(string key, Action<JObject> callbackToRemove)
    {
        if (resultsSub.TryGetValue(key, out var callbacks))
        {
            callbacks.Remove(callbackToRemove);
            if (callbacks.Count == 0)
            {
                resultsSub.Remove(key);
            }
        }
    }

    public void OffAll(string key)
    {
        resultsSub.Remove(key);
    }

    public void On(string key, Action<JObject> callback)
    {
        if (!resultsSub.TryGetValue(key, out var callbacks))
        {
            callbacks = new List<Action<JObject>>();
            resultsSub[key] = callbacks;
        }

        if (!callbacks.Contains(callback))
        {
            callbacks.Add(callback);
        }
    }

    public void SendSocketMessage(string id, object jsonMessage)
    {
        if (!isSocketConnected)
        {
            Debug.LogWarning("Cannot send message. Socket is not connected.");
            return;
        }

        var message = new
        {
            id,
            data = jsonMessage,
        };

        string jsonString = JsonConvert.SerializeObject(message);
        _socket.SendAsync(jsonString, success =>
        {
            if (!success)
            {
                Debug.LogError($"Failed to send message: {jsonString}");
            }
        });
    }

    private void OnDestroy()
    {
        if (_socket != null && _socket.IsAlive)
        {
            _socket.Close();
        }
    }
}
