using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class FromTextureToRealWorld : MonoBehaviour, IPointerClickHandler
{
    [Header("UI Components")]
    [Tooltip("The RawImage component showing the RenderTexture.")]
    public RawImage rawImage;

    [Header("Camera")]
    [Tooltip("The camera rendering to the RenderTexture.")]
    public Camera renderCamera;

    [Header("Spawnable Objects")]
    [Tooltip("List of spawnable objects, each defined as a ScriptableObject.")]
    public List<SpawnableObjectThroughTextureSO> spawnableObjects;

    /// <summary>
    /// This method is triggered when a click is detected on the RawImage.
    /// It calculates the 3D world position based on the click and spawns an object.
    /// </summary>
    /// <param name="eventData">Pointer data from the click event.</param>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Vector3? spawnPosition = CalculateSpawnPosition(eventData);

            // If a valid spawn position was found, spawn an object at that position
            if (spawnPosition.HasValue)
            {
                SpawnObjectAtPosition(spawnPosition.Value);
            }
        }
    }

    /// <summary>
    /// Calculates the 3D world position where the object should be spawned.
    /// This method casts a ray from the 2D click position on the RawImage into the 3D world.
    /// </summary>
    /// <param name="eventData">Pointer data from the click event.</param>
    /// <returns>3D world position to spawn the object, or null if no valid position was found.</returns>
    private Vector3? CalculateSpawnPosition(PointerEventData eventData)
    {
        // Get the RectTransform of the RawImage
        RectTransform rt = rawImage.rectTransform;

        // Convert the click from screen space to local space within the RawImage
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out localPoint);

        // Convert the local point into a normalized coordinate (0 to 1 range)
        Vector2 normalizedPoint = new Vector2(
            (localPoint.x - rt.rect.x) / rt.rect.width,
            (localPoint.y - rt.rect.y) / rt.rect.height
        );

        // Convert the normalized point to a screen point in the RenderTexture's space
        Vector3 screenPoint = new Vector3(
            normalizedPoint.x * renderCamera.pixelWidth,
            normalizedPoint.y * renderCamera.pixelHeight,
            0f
        );

        // Cast a ray from the renderCamera through the clicked point
        Ray ray = renderCamera.ScreenPointToRay(screenPoint);

        // Perform the raycast to find a hit in the 3D world
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            // Get the normal of the surface where the ray hit
            Vector3 surfaceNormal = hit.normal;

            // Determine if the surface is vertical or horizontal based on the y-component of the normal
            bool isVertical = Mathf.Abs(surfaceNormal.y) < 0.5f;

            // Return the calculated hit position
            return hit.point;
        }

        // If no valid position was found, return null
        return null;
    }

    /// <summary>
    /// Spawns a randomly selected object from the list at the given position in the 3D world.
    /// </summary>
    /// <param name="position">The 3D world position where the object should be spawned.</param>
    private void SpawnObjectAtPosition(Vector3 position)
    {
        if (spawnableObjects.Count == 0)
        {
            Debug.LogWarning("No objects available to spawn.");
            return;
        }

        // Choose a random object from the list of spawnable objects
        SpawnableObjectThroughTextureSO spawnableObject = spawnableObjects[Random.Range(0, spawnableObjects.Count)];

        // Get the hit position and adjust based on the object's offsets
        Vector3 adjustedPosition = AdjustSpawnPosition(position, spawnableObject);

        // Instantiate the selected object at the given position
        Instantiate(spawnableObject.prefab, adjustedPosition, Quaternion.identity);

        Debug.Log("Spawned object at: " + adjustedPosition);
    }

    /// <summary>
    /// Adjusts the spawn position based on the surface type and object's specific offsets.
    /// </summary>
    /// <param name="position">Original 3D position based on the raycast hit.</param>
    /// <param name="spawnableObject">The object being spawned, containing prefab and offsets.</param>
    /// <returns>Adjusted 3D position for the spawn.</returns>
    private Vector3 AdjustSpawnPosition(Vector3 position, SpawnableObjectThroughTextureSO spawnableObject)
    {
        // Cast a ray again to get the surface normal for offsetting the object
        Ray ray = renderCamera.ScreenPointToRay(renderCamera.WorldToScreenPoint(position));
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            Vector3 surfaceNormal = hit.normal;

            // Check if the surface is vertical (based on y-component of normal)
            bool isVertical = Mathf.Abs(surfaceNormal.y) < 0.5f;

            // Adjust position based on vertical/horizontal surface
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

        // Return the original position if no hit was found
        return position;
    }
}
