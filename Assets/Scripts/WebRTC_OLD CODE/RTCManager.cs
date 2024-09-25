using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine.UI;
using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine.Events;
using TMPro;

public class RTCManager : MonoBehaviour
{
    [SerializeField] private Camera cameraStream;
    [SerializeField] private RawImage sourceImage;
    [SerializeField]
    private Button joinBtn;
    [SerializeField]
    private Button hostBtn;

    [SerializeField]
    private Button quitBtn;

    [SerializeField]
    private TMP_InputField broadcastIdInput;

    [SerializeField]
    private bool shareForHostAlso = false;


    private Dictionary<string, RTCPeerConnection> connections = new Dictionary<string, RTCPeerConnection>();
    private List<VideoStreamTrack> videoStreamTracks = new List<VideoStreamTrack>();
    private string peerId;
    private List<string> targetPeerId = new List<string>();
    private string status = "";


    private static readonly Queue<Action> _executionQueue = new Queue<Action>();


    public UnityEvent<string> onStatusChange;
    public UnityEvent onJoin;
    public UnityEvent onHost;
    private bool isHosting = false;

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

    public void Awake()
    {
        sourceImage.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(false);
    }

    public void DisableButtons()
    {
        joinBtn.gameObject.SetActive(false);
        hostBtn.gameObject.SetActive(false);
        broadcastIdInput.gameObject.SetActive(false);
        quitBtn.gameObject.SetActive(true);
    }

    public void Start()
    {
        joinBtn.onClick.AddListener(OnClickJoin);
        hostBtn.onClick.AddListener(OnClickHost);
        quitBtn.onClick.AddListener(OnClickQuit); // Add this line
        WebSocketManager.instance.On("GetId", OnReceiveId);
        WebSocketManager.instance.On("RequestOffer", OnRequestOffer);
        status = "Status: Ready to Host or Join";
        onStatusChange.Invoke(status);
        StartCoroutine(WebRTC.Update());
    }

    private void OnReceiveId(JObject jObject)
    {
        peerId = jObject["data"]["id"].ToString();
        Debug.Log($"[RTCManager] Received Peer ID: {peerId}");
    }

    public void OnClickJoin()
    {
        try
        {
            WebSocketManager.instance.On("YouCanJoin", OnJoinAllowance);
            isHosting = false;
            string broadcastId = broadcastIdInput.text; // Get the user-defined broadcast ID
            if (string.IsNullOrEmpty(broadcastId))
            {
                Debug.LogError("[RTCManager] Broadcast ID cannot be empty.");
                return;
            }
            DisableButtons();
            status = "Status: Joining...";
            onStatusChange.Invoke(status);

            WebSocketManager.instance.Connect(() =>
            {
                WebSocketManager.instance.On("GetId", (data) =>
                {
                    peerId = data["data"]["id"].ToString();
                    WebSocketManager.instance.SendSocketMessage("Join_Broadcast", new { broadcastId = broadcastId });
                });
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private void OnJoinAllowance(JObject jObject)
    {
        try
        {
            string peerId = jObject["data"]["peerId"].ToString();
            string broadcastId = jObject["data"]["broadcastId"].ToString();
            Enqueue(() =>
            {
                status = "Status: Socket connected!";
                onStatusChange.Invoke(status);
                InitializePeerConnection(peerId);
                Debug.Log($"[RTCManager] Joining Broadcast with ID: {broadcastId}");
            });
            WebSocketManager.instance.SendSocketMessage("Ready_Join_Broadcast", new { peerId = peerId, broadcastId = broadcastId });
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    public void OnClickQuit()
    {
        try
        {
            // Notify server to quit the broadcast
            Debug.Log("[RTCManager] Quitting Broadcast");

            // Clean up resources
            if (connections != null)
            {
                foreach (KeyValuePair<string, RTCPeerConnection> kvp in connections)
                {
                    if (connections[kvp.Key] != null)
                    {
                        connections[kvp.Key].Close();
                        connections[kvp.Key] = null;
                    }
                }
                connections.Clear();
            }
            if (WebSocketManager.instance.isSocketConnected)
            {
                WebSocketManager.instance.Disconnect();
            }

            // Reset UI and enable other buttons if needed
            ResetUI();
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private void ResetUI()
    {
        hostBtn.gameObject.SetActive(true);
        joinBtn.gameObject.SetActive(true);
        quitBtn.gameObject.SetActive(false);
        broadcastIdInput.text = "";
        broadcastIdInput.gameObject.SetActive(true);
    }

    public void OnClickHost()
    {
        try
        {

            WebSocketManager.instance.On("MakeFirstContact", OnMakeFirstContact);
            string broadcastId = broadcastIdInput.text;
            if (string.IsNullOrEmpty(broadcastId))
            {
                Debug.LogError("[RTCManager] Broadcast ID cannot be empty.");
                return;
            }
            isHosting = true;
            DisableButtons();
            status = "Status: Hosting...";
            onStatusChange.Invoke(status);
            WebSocketManager.instance.Connect(() =>
            {
                WebSocketManager.instance.On("GetId", (data) =>
                {
                    try
                    {
                        Enqueue(() =>
                        {
                            peerId = data["data"]["id"].ToString();
                            Debug.Log($"[RTCManager] Starting Broadcast with ID: {broadcastId}");
                            WebSocketManager.instance.SendSocketMessage("Start_Broadcast", new { broadcastId = broadcastId });
                            status = "Status: Socket connected!";
                            onStatusChange.Invoke(status);
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                        Debug.LogError(e.Message);
                    }
                });
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            Debug.LogError(e.Message);
        }
    }

    public void CoupleHost(string targetpeerId)
    {
        try
        {
            Enqueue(() =>
            {
                InitializePeerConnection(targetpeerId);
                videoStreamTracks.Add(cameraStream.CaptureStreamTrack(1280, 720));
                if (videoStreamTracks == null)
                {
                    Debug.LogError("[RTCManager] Failed to capture video stream from the camera.");
                }
                sourceImage.texture = cameraStream.targetTexture;
                var sender = connections[targetpeerId].AddTrack(videoStreamTracks[videoStreamTracks.Count - 1]);
                if (sender == null)
                {
                    Debug.LogError("[RTCManager] Failed to add video track to the WebRTC connection.");
                }
                else
                {
                    Debug.Log("[RTCManager] Video Track added to WebRTC connection successfully.");
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private RTCConfiguration GetRTCConfiguration()
    {
        RTCConfiguration config = default;
        config.iceServers = new RTCIceServer[]
        {
            new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
            new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } },
            new RTCIceServer { urls = new string[] { "stun:stun2.l.google.com:19302" } },
            new RTCIceServer { urls = new string[] { "stun:stun3.l.google.com:19302" } },
            new RTCIceServer { urls = new string[] { "stun:stun4.l.google.com:19302" } },
            new RTCIceServer {
                urls = new string[] {
                    "turn:64.111.99.141:3222?transport=udp",
                    "turn:64.111.99.141:3222?transport=tcp"
                },
                username = "VecosTurn",
                credential = "TurnPasswordTest"
            },
        };
        return config;
    }

    private void InitializePeerConnection(string targetpeerId)
    {
        try
        {
            var config = GetRTCConfiguration();

            var connection = new RTCPeerConnection(ref config);
            connections.Add(targetpeerId, connection);

            connection.OnIceCandidate = candidate =>
            {
                Debug.Log($"[RTCManager] Sending ICE Candidate: {candidate.Candidate}");
                WebSocketManager.instance.SendSocketMessage("CANDIDATE", new
                {
                    targetId = targetPeerId[0],
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex
                });
            };

            connection.OnIceConnectionChange = state =>
            {
                Debug.Log($"[RTCManager] ICE Connection State Changed: {state}");
                if (state == RTCIceConnectionState.Failed)
                {
                    Debug.LogError("[RTCManager] ICE Connection Failed. Checking candidate pairs.");
                    Enqueue(() =>
                    {
                        StartCoroutine(CheckCandidatePairs(targetpeerId));
                    });
                }
            };

            connection.OnConnectionStateChange = state =>
            {
                Debug.Log($"[RTCManager] Peer Connection State Changed: {state}");
                if (state == RTCPeerConnectionState.Failed)
                {
                    Debug.LogError("[RTCManager] Peer Connection Failed. Attempting to restart ICE.");
                    RestartICE(targetpeerId);
                    status = "Status: " + state.ToString();
                    onStatusChange.Invoke(status);
                }
                else if (state == RTCPeerConnectionState.Disconnected)
                {
                    OnClickQuit();
                }
                else
                {
                    if (state == RTCPeerConnectionState.Connected)
                    {
                        if (isHosting)
                        {
                            onHost.Invoke();
                            if (shareForHostAlso)
                            {
                                sourceImage.gameObject.SetActive(true);
                            }
                        }
                        else
                        {
                            onJoin.Invoke();
                            sourceImage.gameObject.SetActive(true);
                        }
                    }
                    status = "Status: Hosting..." + state.ToString();
                    onStatusChange.Invoke(status);
                }
            };
            connection.OnTrack = e =>
            {
                Debug.Log($"[RTCManager] Track Received: {e.Track.Kind}, Id: {e.Track.Id}");
                if (e.Track is VideoStreamTrack video)
                {
                    Debug.Log("[RTCManager] Video Track Received");
                    video.OnVideoReceived += tex =>
                    {
                        Debug.Log("[RTCManager] Video Frame Received");
                        sourceImage.texture = tex;
                    };
                }
                {
                    Debug.LogError("[RTCManager] Non-video track received.");
                }
            };

            WebSocketManager.instance.On("OFFER", OnReceiveOffer);
            WebSocketManager.instance.On("ANSWER", OnReceiveAnswer);
            WebSocketManager.instance.On("CANDIDATE", OnReceiveCandidate);
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private void RestartICE(string targetpeerId)
    {
        try
        {
            Debug.Log("[RTCManager] Attempting to restart ICE");
            connections[targetpeerId].RestartIce();
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private IEnumerator CheckCandidatePairs(string targetpeerId)
    {
        Debug.Log("[RTCManager] Checking ICE candidate pairs");
        var op = connections[targetpeerId].GetStats();
        yield return op;
        if (!op.IsError)
        {
            foreach (var stat in op.Value.Stats.Values)
            {
                if (stat is RTCIceCandidatePairStats pairStats)
                {
                    Debug.Log($"[RTCManager] Candidate Pair - State: {pairStats.state}, " +
                              $"Local: {pairStats.localCandidateId}, Remote: {pairStats.remoteCandidateId}");
                }
            }
        }
        else
        {
            Debug.LogError($"[RTCManager] Failed to get stats: {op.Error.message}");
        }
    }

    private void OnMakeFirstContact(JObject jObject)
    {
        try
        {
            string targetpeerId = jObject["data"]["peerId"].ToString();
            string broadcastId = jObject["data"]["broadcastId"].ToString();
            targetPeerId.Add(targetpeerId);
            CoupleHost(targetpeerId);
            WebSocketManager.instance.SendSocketMessage("Start_Join_Broadcast", new { targetPeerId = targetpeerId, broadcastId = broadcastId });
            Debug.Log($"[RTCManager] Joining Broadcast and making contact with : {targetpeerId}");
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private void OnRequestOffer(JObject jObject)
    {
        try
        {
            targetPeerId.Add(jObject["data"]["peerId"].ToString());
            Debug.Log($"[RTCManager] Request Offer from Peer ID: {jObject["data"]["peerId"].ToString()}");

            Enqueue(() =>
            {
                StartCoroutine(CreateOffer(jObject["data"]["peerId"].ToString()));
            });
        }
        catch (Exception e)
        {
            Debug.Log(e.Message);
            Debug.Log(e);
        }
    }

    private void OnReceiveOffer(JObject jObject)
    {
        try
        {
            Debug.Log("[RTCManager] Offer Received");

            targetPeerId.Add(jObject["fromId"].ToString());
            var offerSessionDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = jObject["data"]["sdp"].ToString()
            };

            Enqueue(() =>
            {
                StartCoroutine(HandleReceivedOffer(offerSessionDesc, jObject["fromId"].ToString()));
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private IEnumerator HandleReceivedOffer(RTCSessionDescription offerSessionDesc, string target)
    {

        Debug.Log("[RTCManager] Handling Received Offer");

        var remoteDescOp = connections[target].SetRemoteDescription(ref offerSessionDesc);
        yield return remoteDescOp;

        var answer = connections[target].CreateAnswer();
        yield return answer;

        var answerDesc = answer.Desc;
        var localDescOp = connections[target].SetLocalDescription(ref answerDesc);
        yield return localDescOp;

        WebSocketManager.instance.SendSocketMessage("ANSWER", new
        {
            targetId = targetPeerId[0],
            type = answerDesc.type.ToString(),
            sdp = answerDesc.sdp
        });

        Debug.Log("[RTCManager] Answer Sent");
    }

    private void OnReceiveAnswer(JObject jObject)
    {
        try
        {
            Debug.Log("[RTCManager] Answer Received");

            var answerSessionDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = jObject["data"]["sdp"].ToString()
            };

            Enqueue(() =>
            {
                StartCoroutine(HandleReceivedAnswer(answerSessionDesc, jObject["fromId"].ToString()));
            });
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private IEnumerator HandleReceivedAnswer(RTCSessionDescription answerSessionDesc, string target)
    {
        Debug.Log("[RTCManager] Handling Received Answer");

        var remoteDescOp = connections[target].SetRemoteDescription(ref answerSessionDesc);
        yield return remoteDescOp;
    }

    private void OnReceiveCandidate(JObject jObject)
    {
        try { 
        Debug.Log($"[RTCManager] ICE Candidate Received: {jObject["data"]["candidate"].ToString()}");

        var init = new RTCIceCandidateInit
        {
            candidate = jObject["data"]["candidate"].ToString(),
            sdpMid = jObject["data"]["sdpMid"].ToString(),
            sdpMLineIndex = int.Parse(jObject["data"]["sdpMLineIndex"].ToString())
        };

        var candidate = new RTCIceCandidate(init);
        Debug.Log(jObject["fromId"]);
        Debug.Log(connections[jObject["fromId"].ToString()]);
        connections[jObject["fromId"].ToString()].AddIceCandidate(candidate);
        Debug.Log("[RTCManager] ICE Candidate Added");
        }catch(Exception e)
        {
            Debug.LogError(e.Message);
            Debug.LogError(e);
        }
    }

    private IEnumerator CreateOffer(string targetPeerId)
    {
        Debug.Log("[RTCManager] Creating Offer");

        var offer = connections[targetPeerId].CreateOffer();
        yield return offer;

        var offerDesc = offer.Desc;
        var localDescOp = connections[targetPeerId].SetLocalDescription(ref offerDesc);
        yield return localDescOp;

        WebSocketManager.instance.SendSocketMessage("OFFER", new
        {
            targetId = targetPeerId,
            type = offerDesc.type.ToString(),
            sdp = offerDesc.sdp
        });

        Debug.Log("[RTCManager] Offer Sent");
    }

    public void OnDestroy()
    {
        Debug.Log("[RTCManager] Destroying Peer Connection");

        if (videoStreamTracks != null)
        {
            foreach (VideoStreamTrack track in videoStreamTracks)
            {
                if (track != null)
                {
                    track.Stop();
                }
            }
        }

        if (connections != null)
        {
            foreach (KeyValuePair<string, RTCPeerConnection> connection in connections)
            {
                if (connection.Value != null)
                {
                    connection.Value.Close();
                }

            }

        }

    }
}