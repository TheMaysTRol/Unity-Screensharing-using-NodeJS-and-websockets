using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Events;

/// <summary>
/// Manages WebSocket connections, sending and receiving messages, and event subscriptions.
/// </summary>
public class WebSocketManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("WebSocket Configuration")]
    [Tooltip("IP address or URL of the WebSocket server.")]
    public string socketServer = "127.0.0.1";

    [Tooltip("Port number of the WebSocket server.")]
    public string socketPort = "3000";

    #endregion

    #region Private Fields

    private WebSocket _socket;
    private Dictionary<string, List<Action<JObject>>> resultsSub = new Dictionary<string, List<Action<JObject>>>();
    public bool isSocketConnected = false;

    #endregion

    #region Public Fields

    public static WebSocketManager instance;
    public UnityEvent OnSocketConnect;
    public UnityEvent<string> OnSocketDisconnect;
    private string mySocketID = "";

    #endregion

    #region Unity Lifecycle Methods

    /// <summary>
    /// Ensures that only one instance of WebSocketManager exists and prevents destruction on scene loads.
    /// </summary>
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

    /// <summary>
    /// Initializes the WebSocket and subscribes to initial events when the script starts.
    /// </summary>
    private void Start()
    {
        InitializeWebSocket();
        SubscribeToEvents();
    }

    /// <summary>
    /// Cleans up the WebSocket connection if the object is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (_socket != null && _socket.IsAlive)
        {
            _socket.Close();
        }
    }

    #endregion

    #region WebSocket Initialization

    /// <summary>
    /// Initializes the WebSocket and attaches event listeners for socket communication.
    /// </summary>
    private void InitializeWebSocket()
    {
        _socket = new WebSocket(WebSocketUrl);
        _socket.OnOpen += OnSocketConnected;
        _socket.OnMessage += OnSocketReceiveMessage;
        _socket.OnError += OnSocketError;
        _socket.OnClose += OnSocketDisconnected;
    }

    /// <summary>
    /// Generates the WebSocket URL using the configured server IP and port.
    /// </summary>
    private string WebSocketUrl => $"ws://{socketServer}:{socketPort}";

    /// <summary>
    /// Subscribes to default events like retrieving the socket ID.
    /// </summary>
    private void SubscribeToEvents()
    {
        On("ReceivePlayerId", (json) =>
        {
            mySocketID = json["data"]["id"].ToString();
        });
    }

    #endregion

    #region WebSocket Connection Methods

    /// <summary>
    /// Connects to the WebSocket server asynchronously and invokes the provided action upon successful connection.
    /// </summary>
    /// <param name="onConnected">Action to invoke when the socket is connected.</param>
    public void Connect(Action onConnected)
    {
        _socket.OnOpen += (sender, e) => onConnected?.Invoke();
        _socket.ConnectAsync();
    }

    /// <summary>
    /// Disconnects from the WebSocket server asynchronously.
    /// </summary>
    public void Disconnect()
    {
        _socket.CloseAsync();
        isSocketConnected = false;
    }

    #endregion

    #region WebSocket Event Handlers

    /// <summary>
    /// Called when the WebSocket connection is established.
    /// </summary>
    private void OnSocketConnected(object sender, EventArgs e)
    {
        Debug.Log("Socket connected.");
        OnSocketConnect?.Invoke();
        isSocketConnected = true;
    }

    /// <summary>
    /// Called when the WebSocket is disconnected, providing the reason for disconnection.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">Contains the reason for disconnection.</param>
    private void OnSocketDisconnected(object sender, CloseEventArgs e)
    {
        OnSocketDisconnect?.Invoke(e.Reason);
        isSocketConnected = false;
        Debug.Log($"Socket disconnected: {e.Reason}");
    }

    /// <summary>
    /// Called when an error occurs during the WebSocket connection.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">Contains error details.</param>
    private void OnSocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogError($"Socket error: {e.Message}");
    }

    /// <summary>
    /// Called when a message is received from the WebSocket server. Parses the JSON and triggers callbacks.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="message">The message event containing the data.</param>
    private void OnSocketReceiveMessage(object sender, MessageEventArgs message)
    {
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

    #endregion

    #region Event Subscription Methods

    /// <summary>
    /// Subscribes a callback to a specific event key.
    /// </summary>
    /// <param name="key">The event key to subscribe to.</param>
    /// <param name="callback">The callback action to invoke when the event occurs.</param>
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

    /// <summary>
    /// Unsubscribes a specific callback from a specific event key.
    /// </summary>
    /// <param name="key">The event key.</param>
    /// <param name="callbackToRemove">The callback to remove from the event key.</param>
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

    /// <summary>
    /// Unsubscribes all callbacks associated with a specific event key.
    /// </summary>
    /// <param name="key">The event key to unsubscribe from.</param>
    public void OffAll(string key)
    {
        resultsSub.Remove(key);
    }

    #endregion

    #region WebSocket Message Sending

    /// <summary>
    /// Sends a message to the WebSocket server with a specified ID and JSON content.
    /// </summary>
    /// <param name="id">The message ID used for event handling.</param>
    /// <param name="jsonMessage">The message content to send.</param>
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

    #endregion
}
