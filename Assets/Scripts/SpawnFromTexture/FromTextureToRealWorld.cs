using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System;

public class FromTextureToRealWorld : MonoBehaviour, IPointerClickHandler
{
    [Header("UI Components")]
    public RawImage rawImage;

    [Header("Camera")]
    public Camera mainCamera;

    [Header("Spawnable Objects")]
    public List<SpawnableObjectThroughTextureSO> spawnableObjects;
    [SerializeField] private Transform spawnObjectsParent;

    [SerializeField] private VideoStreamManager videoStreamManager;

    private int selectedObjectID = 0;

    public enum DeviceType
    {
        Mobile,
        Desktop,
        VR
    }

    [Header("Device Settings")]
    public DeviceType currentDeviceType;

    [Header("VR Settings")]
    [Tooltip("The camera used for VR rendering (usually the center eye anchor)")]
    public Camera vrCamera;

    [Tooltip("Reference to the VR player's head transform")]
    public Transform vrHeadTransform;

    private void Start()
    {
        if (videoStreamManager.IsConnected())
        {
            WebSocketManager.instance.On("Instantiate", OnReceiveInstantiation);
        }

        if (currentDeviceType == DeviceType.Desktop && Application.isMobilePlatform)
        {
            currentDeviceType = DeviceType.Mobile;
            Debug.Log("Auto-detected Mobile platform");
        }
    }

    private void OnReceiveInstantiation(JObject Jobject)
    {
        try
        {
            int objectID = int.Parse(Jobject["data"]["data"]["ObjectID"].ToString());
            Vector2 normalizedPosition = new Vector2(
                (float)Jobject["data"]["data"]["Position"]["x"],
                (float)Jobject["data"]["data"]["Position"]["y"]
            );
            Debug.Log($"Received instantiation request: ObjectID={objectID}, NormalizedPosition=({normalizedPosition.x}, {normalizedPosition.y})");

            Vector3? spawnPosition = CalculateSpawnPosition(normalizedPosition);
            if (spawnPosition.HasValue)
            {
                InstantiateObject(objectID, spawnPosition.Value);
            }
            else
            {
                Debug.LogWarning("Failed to calculate a valid spawn position.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in OnReceiveInstantiation: {e.Message}");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (videoStreamManager != null && videoStreamManager.IsConnected() && eventData.button == PointerEventData.InputButton.Left)
        {
            Vector2 normalizedPosition = NormalizePointerPosition(eventData.position);
            SpawnObjectAtPosition(selectedObjectID, normalizedPosition);
        }
    }

    private Vector2 NormalizePointerPosition(Vector2 pointerPosition)
    {
        RectTransform rt = rawImage.rectTransform;
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, pointerPosition, null, out localPoint);

        return Rect.PointToNormalized(rt.rect, localPoint);
    }

    private Vector3? CalculateSpawnPosition(Vector2 normalizedPosition)
    {
        switch (currentDeviceType)
        {
            case DeviceType.Mobile:
            case DeviceType.Desktop:
                return CalculateSpawnPositionScreen(normalizedPosition);
            case DeviceType.VR:
                return CalculateSpawnPositionVR(normalizedPosition);
            default:
                Debug.LogError("Unknown device type");
                return null;
        }
    }

    private Vector3? CalculateSpawnPositionScreen(Vector2 normalizedPosition)
    {
        Vector3 screenPoint = new Vector3(
            normalizedPosition.x * mainCamera.pixelWidth,
            normalizedPosition.y * mainCamera.pixelHeight,
            mainCamera.nearClipPlane
        );

        Ray ray = mainCamera.ScreenPointToRay(screenPoint);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Debug.Log($"Screen Raycast hit at world position: {hit.point}");
            return hit.point;
        }

        Debug.LogWarning("Screen Raycast did not hit any collider.");
        return null;
    }

    private Vector3? CalculateSpawnPositionVR(Vector2 normalizedPosition)
    {
        // Use the ViewportPointToRay method instead of manually adjusting the ray direction
        Ray ray = vrCamera.ViewportPointToRay(new Vector3(normalizedPosition.x, normalizedPosition.y, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Debug.Log($"VR Raycast hit at world position: {hit.point}");
            return hit.point;
        }

        Debug.LogWarning("VR Raycast did not hit any collider.");
        return null;
    }

    private void SpawnObjectAtPosition(int objectID, Vector2 normalizedPosition)
    {
        if (spawnableObjects.Count == 0)
        {
            Debug.LogWarning("No objects available to spawn.");
            return;
        }

        if (videoStreamManager.isHosting())
        {
            Vector3? spawnPosition = CalculateSpawnPosition(normalizedPosition);
            if (spawnPosition.HasValue)
            {
                InstantiateObject(objectID, spawnPosition.Value);
            }
            else
            {
                Debug.LogWarning("Failed to calculate a valid spawn position.");
            }
        }
        else if (videoStreamManager.IsJoined())
        {
            WebSocketManager.instance.SendSocketMessage("Instantiate", new { ObjectID = objectID, Position = new { x = normalizedPosition.x, y = normalizedPosition.y } });
        }
    }

    public void InstantiateObject(int objectID, Vector3 position)
    {
        if (objectID < 0 || objectID >= spawnableObjects.Count)
        {
            Debug.LogError($"Invalid objectID: {objectID}");
            return;
        }

        SpawnableObjectThroughTextureSO spawnableObject = spawnableObjects[objectID];
        Vector3 adjustedPosition = AdjustSpawnPosition(position, spawnableObject);

        GameObject spawnedObject = Instantiate(spawnableObject.prefab, adjustedPosition, Quaternion.identity, spawnObjectsParent);
        Debug.Log($"Spawned object {spawnableObject.prefab.name} at: {adjustedPosition}");
    }

    private Vector3 AdjustSpawnPosition(Vector3 position, SpawnableObjectThroughTextureSO spawnableObject)
    {
        Ray ray = new Ray(mainCamera.transform.position, position - mainCamera.transform.position);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            Vector3 surfaceNormal = hit.normal;
            bool isVertical = Mathf.Abs(surfaceNormal.y) < 0.5f;

            if (isVertical)
            {
                return position + surfaceNormal * spawnableObject.verticalOffset;
            }
            else
            {
                position.y += spawnableObject.groundOffset;
                return position;
            }
        }

        return position;
    }

    public void SetSelectedObject(int index)
    {
        selectedObjectID = index;
    }
}