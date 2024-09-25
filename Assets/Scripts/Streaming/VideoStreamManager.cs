using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages video streaming functionality, handling WebSocket connections, camera streaming, and UI updates.
/// </summary>
public class VideoStreamManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("Video Related")]
    [SerializeField] private Camera streamCamera;
    [SerializeField] private int frameRate = 30;
    [SerializeField] private int quality = 75;

    [Header("UI Elements")]
    [SerializeField] private TMP_InputField joinInputField;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button LeaveButton;
    [SerializeField] private TMP_InputField fpsInputField;
    [SerializeField] private TMP_InputField qualityInputField;
    [SerializeField] private RawImage displayImage;
    [SerializeField] private CanvasGroup totalUi;
    [SerializeField] private TextMeshProUGUI statusMessage;

    [SerializeField] private bool debugMessages = false;
    [SerializeField] private bool testLocal = false;

    #endregion

    #region Private Fields

    private WebSocketManager webSocketManager;
    private byte[] imageData;
    private float frameTime;
    private bool isHost = false;
    private RenderTexture renderTexture;
    private Texture2D texture2D;
    private Texture2D texture;

    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    #endregion

    #region Unity Lifecycle Methods

    /// <summary>
    /// Initializes WebSocket manager and sets up UI listeners and frame rate.
    /// </summary>
    private void Start()
    {
        InitializeWebSocketManager();
        SetupUIListeners();
        InitializeFrameRate();
    }

    /// <summary>
    /// Processes the execution queue during each frame update.
    /// </summary>
    private void Update()
    {
        ProcessExecutionQueue();
    }

    #endregion

    #region Initialization Methods

    /// <summary>
    /// Initializes WebSocket manager and adds event listeners for socket communication.
    /// </summary>
    private void InitializeWebSocketManager()
    {
        webSocketManager = WebSocketManager.instance;
        webSocketManager.OnSocketDisconnect.AddListener(OnBroadcastDisconnect);
        webSocketManager.On("ReceivePlayerId", OnReceivePlayerId);
    }

    /// <summary>
    /// Sets up button and input field event listeners for UI interactions.
    /// </summary>
    private void SetupUIListeners()
    {
        joinButton.onClick.AddListener(OnJoinButtonClicked);
        LeaveButton.onClick.AddListener(OnLeaveButtonClicked);
        qualityInputField.onValueChanged.AddListener(OnChangeQuality);
        fpsInputField.onValueChanged.AddListener(OnChangeFPS);
    }

    /// <summary>
    /// Initializes the frame rate and updates the corresponding UI fields.
    /// </summary>
    private void InitializeFrameRate()
    {
        frameTime = 1.0f / frameRate;
        fpsInputField.text = frameRate.ToString();
        qualityInputField.text = quality.ToString();
    }

    #endregion

    #region UI Methods

    /// <summary>
    /// Updates the status message on the UI.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public void SetStatusMessage(string message)
    {
        Enqueue(() => statusMessage.text = "Status: " + message);
    }

    /// <summary>
    /// Enables or disables user interaction with the UI elements.
    /// </summary>
    /// <param name="active">True to enable UI interaction, false to disable.</param>
    public void SetInteractUI(bool active)
    {
        totalUi.interactable = active;
    }

    /// <summary>
    /// Toggles the visibility of various UI elements when joining or leaving a room.
    /// </summary>
    /// <param name="active">True to activate room join UI, false to show streaming UI.</param>
    public void SetActivateUI(bool active)
    {
        joinInputField.gameObject.SetActive(active);
        joinButton.gameObject.SetActive(active);
        displayImage.gameObject.SetActive(!active);
        LeaveButton.gameObject.SetActive(!active);
        qualityInputField.gameObject.SetActive(!active);
        fpsInputField.gameObject.SetActive(!active);
        joinInputField.text = "";
    }

    #endregion

    #region Button Click Handlers

    /// <summary>
    /// Handles the join button click event, validates the room name, and connects to the WebSocket server.
    /// </summary>
    private void OnJoinButtonClicked()
    {
        if (!IsRoomNameValid()) return;
        SetInteractUI(false);
        SetStatusMessage("Connecting to server...");
        webSocketManager.Connect(OnConnectedToSocket);
    }

    /// <summary>
    /// Handles the leave button click event and disconnects from the WebSocket server.
    /// </summary>
    private void OnLeaveButtonClicked()
    {
        webSocketManager.Disconnect();
    }

    #endregion

    #region WebSocket Event Handlers

    /// <summary>
    /// Called when successfully connected to the WebSocket server.
    /// </summary>
    private void OnConnectedToSocket()
    {
        DebugMessage("Connected to WebSocket.");
        webSocketManager.On("BroadcastFatalError", BroadcastFatalError);
        SetStatusMessage("Connected to server");
    }

    /// <summary>
    /// Handles receiving the player ID from the server after joining a room.
    /// </summary>
    /// <param name="json">The JSON object containing the player ID.</param>
    private void OnReceivePlayerId(JObject json)
    {
        string playerId = json["data"]["id"].ToString();
        SendRoomNameToServer();
    }

    /// <summary>
    /// Handles the result of joining a broadcast, updating the UI accordingly.
    /// </summary>
    /// <param name="Jobject">The JSON object containing the result of the join request.</param>
    private void OnJoinBroadcastResult(JObject Jobject)
    {
        Enqueue(() =>
        {
            try
            {
                SetActivateUI(false);
                SetInteractUI(true);
                webSocketManager.Off("JoinBroadcastResult", OnJoinBroadcastResult);
                webSocketManager.On("BroadcastDisconnect", OnBroadcastDisconnect);
                DebugMessage(Jobject["message"].ToString());
                isHost = (bool)Jobject["isHost"];

                if (isHost)
                {
                    SetStatusMessage("Successfully hosted room " + Jobject["broadcastId"].ToString());
                    if (!testLocal) displayImage.gameObject.SetActive(false);
                    StartStream();
                }
                else
                {
                    SetStatusMessage("Successfully joined room " + Jobject["broadcastId"].ToString());
                    webSocketManager.On("StreamResult", OnStreamResult);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ex.Message}\n{ex}");
            }
        });
    }

    /// <summary>
    /// Handles the broadcast disconnect event from the server.
    /// </summary>
    /// <param name="Jobject">The JSON object containing the disconnect message.</param>
    private void OnBroadcastDisconnect(JObject Jobject)
    {
        Enqueue(() =>
        {
            try
            {
                DebugMessage(Jobject["message"].ToString());
                DisconnectAndResetUI(Jobject["message"].ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"{e.Message}\n{e}");
            }
        });
    }

    /// <summary>
    /// Handles the broadcast disconnect event with a simple message.
    /// </summary>
    /// <param name="msg">The disconnect message.</param>
    private void OnBroadcastDisconnect(string msg)
    {
        Enqueue(() =>
        {
            try
            {
                DebugMessage("You've been disconnected from the broadcast");
                DisconnectAndResetUI("You've been disconnected from the broadcast");
            }
            catch (Exception e)
            {
                Debug.LogError($"{e.Message}\n{e}");
            }
        });
    }

    /// <summary>
    /// Handles receiving the streaming result, such as image data from the server.
    /// </summary>
    /// <param name="Jobject">The JSON object containing the stream result.</param>
    private void OnStreamResult(JObject Jobject)
    {
        Enqueue(() =>
        {
            try
            {
                string images = Jobject["data"]["data"].ToString();
                ReceiveImages(images);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ex.Message}\n{ex}");
            }
        });
    }

    #endregion

    #region Streaming Methods

    /// <summary>
    /// Starts the video stream from the camera.
    /// </summary>
    private void StartStream()
    {
        try
        {
            InitializeRenderTexture();
            StartCoroutine(StreamVideo());
        }
        catch (Exception ex)
        {
            Debug.LogError($"{ex.Message}\n{ex}");
        }
    }

    /// <summary>
    /// Coroutine to continuously stream the video at the specified frame rate.
    /// </summary>
    private IEnumerator StreamVideo()
    {
        while (true)
        {
            yield return new WaitForSeconds(frameTime);
            CaptureCameraImage();
            imageData = texture2D.EncodeToJPG(quality);
            string b64 = Convert.ToBase64String(imageData);

            if (testLocal && isHost) ReceiveImages(b64);
            if (webSocketManager.isSocketConnected && isHost)
            {
                webSocketManager.SendSocketMessage("Stream", new { data = b64 });
            }
        }
    }

    /// <summary>
    /// Captures the camera image and stores it in the texture.
    /// </summary>
    private void CaptureCameraImage()
    {
        RenderTexture.active = renderTexture;
        streamCamera.Render();
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();
        RenderTexture.active = null;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Enqueues an action to be executed on the main thread.
    /// </summary>
    /// <param name="action">The action to enqueue.</param>
    private static void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// Processes and executes all queued actions.
    /// </summary>
    private void ProcessExecutionQueue()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// Logs debug messages if debug mode is enabled.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private void DebugMessage(string message)
    {
        if (debugMessages)
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// Checks if the room name is valid.
    /// </summary>
    /// <returns>True if the room name is valid, false otherwise.</returns>
    private bool IsRoomNameValid()
    {
        if (string.IsNullOrEmpty(joinInputField.text))
        {
            SetStatusMessage("Room name must not be empty");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends the room name to the server to join a broadcast.
    /// </summary>
    private void SendRoomNameToServer()
    {
        string roomName = joinInputField.text;
        var message = new { roomName };
        webSocketManager.On("JoinBroadcastResult", OnJoinBroadcastResult);
        SetStatusMessage("Joining room " + roomName);
        webSocketManager.SendSocketMessage("JoinBroadcast", message);
        DebugMessage("Sent Room Name: " + roomName);
    }

    /// <summary>
    /// Disconnects from the WebSocket and resets the UI.
    /// </summary>
    /// <param name="statusMessage">The status message to display after disconnection.</param>
    private void DisconnectAndResetUI(string statusMessage)
    {
        if (webSocketManager.isSocketConnected)
        {
            webSocketManager.Disconnect();
        }
        SetStatusMessage(statusMessage);
        SetActivateUI(true);
    }

    /// <summary>
    /// Initializes the render texture for capturing camera output.
    /// </summary>
    private void InitializeRenderTexture()
    {
        renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        texture2D = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        streamCamera.targetTexture = renderTexture;
    }

    #endregion

    #region Image Handling Methods

    /// <summary>
    /// Processes and displays received image data in byte array format.
    /// </summary>
    /// <param name="imageData">The image data in byte array format.</param>
    public void ReceiveImages(byte[] imageData)
    {
        if (texture == null)
        {
            texture = new Texture2D(2, 2);
        }
        texture.LoadImage(imageData);
        displayImage.texture = texture;
    }

    /// <summary>
    /// Processes and displays received image data in base64 string format.
    /// </summary>
    /// <param name="b64">The base64 string representation of the image data.</param>
    public void ReceiveImages(string b64)
    {
        b64 = b64.Replace("\n", "").Replace("\r", "");
        byte[] imageData = Convert.FromBase64String(b64);
        ReceiveImages(imageData);
    }

    #endregion

    #region UI Event Handlers

    /// <summary>
    /// Handles changes in the FPS input field and updates the frame rate.
    /// </summary>
    /// <param name="text">The new FPS value as a string.</param>
    private void OnChangeFPS(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        frameRate = int.Parse(text);
        frameTime = 1.0f / frameRate;
    }

    /// <summary>
    /// Handles changes in the quality input field and updates the image quality.
    /// </summary>
    /// <param name="text">The new quality value as a string.</param>
    private void OnChangeQuality(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        quality = int.Parse(text);
    }

    /// <summary>
    /// Handles a fatal error broadcast from the server, disconnects, and logs the error message.
    /// </summary>
    /// <param name="Jobject">The JSON object containing the fatal error message.</param>
    private void BroadcastFatalError(JObject Jobject)
    {
        SetStatusMessage(Jobject["message"].ToString());
        webSocketManager.Disconnect();
        DebugMessage($"{Jobject["message"]} - Fatal error from server, disconnected");
    }

    #endregion
}
