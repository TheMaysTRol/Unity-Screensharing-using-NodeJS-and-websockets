using Newtonsoft.Json.Linq;
using System.Collections;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VideoStreamManager : MonoBehaviour
{
    [Header("Video Related")]
    [SerializeField] private Camera streamCamera;  // Reference to the game camera
    [SerializeField] private int frameRate = 30;   // Frame rate for streaming
    [SerializeField] private int quality = 75;     // Quality of the streamed image (1-100)


    [Header("UI Elements")]
    [SerializeField] private TMP_InputField joinInputField; // Input field for room name
    [SerializeField] private Button joinButton;          // Button to join a room
    [SerializeField] private Button LeaveButton;          // Button to leave a room
    [SerializeField] private TMP_InputField fpsInputField;
    [SerializeField] private TMP_InputField qualityInputField;
    [SerializeField] private RawImage displayImage;
    [SerializeField] private CanvasGroup totalUi;
    [SerializeField] private TextMeshProUGUI statusMessage;


    private WebSocketManager webSocketManager;
    private byte[] imageData;
    private float frameTime;
    private bool isHost = false;
    public bool testLocal = false;
    private RenderTexture renderTexture;
    private Texture2D texture2D;

    [SerializeField] private bool debugMessages = false;


    public void SetStatusMessage(string message)
    {
        Enqueue(() =>
        {
            statusMessage.text = "Status: " + message;
        });
    }

    public void SetInteractUI(bool active)
    {
        totalUi.interactable = active;
    }

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


    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    public static void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    private void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                var action = _executionQueue.Dequeue();
                action.Invoke();
            }
        }

    }

    private void DebugMessage(string message)
    {
        if (debugMessages)
        {
            Debug.Log(message);
        }
    }

    // Start is called before the first frame update

    public void Start()
    {
        // Ensure the WebSocketManager is accessible
        webSocketManager = WebSocketManager.instance;
        webSocketManager.OnSocketDisconnect.AddListener(OnBroadcastDisconnect);

        // Subscribe to button click events
        joinButton.onClick.AddListener(OnJoinButtonClicked);
        LeaveButton.onClick.AddListener(OnLeaveButtonClicked);

        qualityInputField.onValueChanged.AddListener(OnChangeQuality);
        fpsInputField.onValueChanged.AddListener(OnChangeFPS);

        // Subscribe to the event for receiving player ID
        webSocketManager.On("ReceivePlayerId", OnReceivePlayerId);
        // Setup frame time based on the target frame rate
        frameTime = 1.0f / frameRate;

        fpsInputField.text = frameRate.ToString();
        qualityInputField.text = quality.ToString();
    }


    private void OnChangeFPS(string text)
    {
        if (text == "")
        {
            return;
        }
        frameRate = int.Parse(text);
    }

    private void OnChangeQuality(string text)
    {
        if (text == "")
        {
            return;
        }
        quality = int.Parse(text);
    }

    private void OnLeaveButtonClicked()
    {
        webSocketManager.Disconnect();
    }

    private bool isRoomNameValid()
    {
        if (joinInputField.text.Length > 0)
        {
            return true;
        }
        return false;
    }

    private void OnJoinButtonClicked()
    {
        if (!isRoomNameValid())
        {
            SetStatusMessage("Room name must not be empty");
            return;
        }
        // Connect to the socket and subscribe to receive player ID
        SetInteractUI(false);
        SetStatusMessage("Connecting to server...");
        webSocketManager.Connect(OnConnectedToSocket);
    }

    private void OnConnectedToSocket()
    {
        DebugMessage("Connected to WebSocket.");
        webSocketManager.On("BroadcastFatalError", BroadcastFatalError);
        SetStatusMessage("Connected to server");
    }

    private void BroadcastFatalError(JObject Jobject)
    {
        SetStatusMessage(Jobject["message"].ToString());
        webSocketManager.Disconnect();
        DebugMessage(Jobject["message"].ToString());
        DebugMessage("Fatal error from server , disconnected");
    }

    private void OnReceivePlayerId(JObject json)
    {
        // Extract the player ID from the received message
        string playerId = json["data"]["id"].ToString();

        // After receiving the player ID, send the room name to the server
        SendRoomNameToServer();
    }

    private void SendRoomNameToServer()
    {
        string roomName = joinInputField.text;

        // Prepare the message to send
        var message = new
        {
            roomName = roomName,
        };

        // Send the message to the server
        webSocketManager.On("JoinBroadcastResult", OnJoinBroadcastResult);
        SetStatusMessage("Joining room " + roomName);
        webSocketManager.SendSocketMessage("JoinBroadcast", message);
        DebugMessage("Sent Room Name: " + roomName);
    }

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
                    SetStatusMessage("Succesfully hosted room " + Jobject["broadcastId"].ToString());
                    if (!testLocal)
                    {
                        displayImage.gameObject.SetActive(false);
                    }
                    StartStream();
                }
                else
                {
                    SetStatusMessage("Succesfully joined room " + Jobject["broadcastId"].ToString());
                    webSocketManager.On("StreamResult", OnStreamResult);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message.ToString());
                Debug.LogError(ex.ToString());
            }
        });
    }

    private void OnBroadcastDisconnect(JObject Jobject)
    {
        Enqueue(() =>
        {
            try
            {
                DebugMessage(Jobject["message"].ToString());
                if (webSocketManager.isSocketConnected)
                {
                    webSocketManager.Disconnect();
                }
                SetStatusMessage(Jobject["message"].ToString());
                SetActivateUI(true);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message.ToString());
                Debug.LogError(e);
            }
        });
    }

    private void OnBroadcastDisconnect(string msg)
    {
        Enqueue(() =>
        {
            try
            {
                DebugMessage("You've been disconnected from the broadcast");
                if (webSocketManager.isSocketConnected)
                {
                    webSocketManager.Disconnect();
                }
                SetStatusMessage("You've been disconnected from the broadcast");
                SetActivateUI(true);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message.ToString());
                Debug.LogError(e);
            }
        });
    }


    private void OnStreamResult(JObject Jobject)
    {
        Enqueue(() =>
        {
            try
            {
                string images = Jobject["data"]["data"].ToString();
                RecieveImages(images);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
                Debug.LogError(ex);
            }
        });
    }

    void StartStream()
    {
        try
        {
            // Setup the RenderTexture and Texture2D for capturing the camera image
            renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
            texture2D = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);

            // Assign the renderTexture to the camera
            streamCamera.targetTexture = renderTexture;

            // Start streaming coroutine
            StartCoroutine(StreamVideo());
        }
        catch (Exception ex)
        {
            Debug.LogError($"{ex.Message}");
            Debug.LogError($"{ex}");
        }
    }

    private Texture2D texture;
    // Coroutine to handle streaming at a set frame rate
    IEnumerator StreamVideo()
    {
        while (true)
        {
            // Wait for the next frame based on the defined frame rate
            yield return new WaitForSeconds(frameTime);
            // Capture the image from the camera
            CaptureCameraImage();

            // Encode the image to a byte array (using JPG format)
            imageData = texture2D.EncodeToJPG(quality);
            string b64 = System.Convert.ToBase64String(imageData);
            if (testLocal && isHost)
            {
                RecieveImages(b64);
            }

            if (webSocketManager.isSocketConnected && isHost)
            {
                webSocketManager.SendSocketMessage("Stream", new { data = b64 });
            }

        }
    }

    public void RecieveImages(byte[] imageData)
    {
        if (texture == null)
        {
            // Create a new Texture2D
            texture = new Texture2D(2, 2); // Start with a small texture size
        }

        // Load the image data into the texture
        texture.LoadImage(imageData);
        // Assign the texture to the RawImage
        displayImage.texture = texture;
    }

    public void RecieveImages(string b64)
    {
        b64 = b64.Replace("\n", "").Replace("\r", "");
        byte[] imageData = System.Convert.FromBase64String(b64);
        if (texture == null)
        {
            // Create a new Texture2D
            texture = new Texture2D(2, 2); // Start with a small texture size
        }

        // Load the image data into the texture
        texture.LoadImage(imageData);
        // Assign the texture to the RawImage
        displayImage.texture = texture;
    }

    // Capture the camera image and store it in texture2D
    void CaptureCameraImage()
    {
        // Set the camera to render into the RenderTexture
        RenderTexture.active = renderTexture;
        streamCamera.Render();

        // Copy the RenderTexture to the Texture2D
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();

        // Clear the active RenderTexture
        RenderTexture.active = null;
    }

}
