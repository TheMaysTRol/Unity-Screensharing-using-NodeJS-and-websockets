using UnityEngine;

[CreateAssetMenu(fileName = "SpawnableObjectThroughTextureSO", menuName = "SpawnableObjectThroughTextureSO/Spawnable Object", order = 1)]
public class SpawnableObjectThroughTextureSO : ScriptableObject
{
    [Tooltip("The prefab to spawn.")]
    public GameObject prefab;

    public Sprite imageIcon;

    [Tooltip("Distance to offset the object from vertical surfaces.")]
    public float verticalOffset = 0.1f;

    [Tooltip("Distance to offset the object above horizontal surfaces.")]
    public float groundOffset = 0.5f;
}
