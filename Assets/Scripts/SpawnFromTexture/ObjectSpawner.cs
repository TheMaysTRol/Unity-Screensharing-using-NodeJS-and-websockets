using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [SerializeField] private FromTextureToRealWorld objectDetector;
    [SerializeField] private Transform spawnContent;
    [SerializeField] private SpawnableItem uiElementPrefab;

    SpawnableItem lastSelectedItem = null;

    public void Start()
    {
        CreateObjectsListUI();
    }

    private void CreateObjectsListUI()
    {
        int i = 0;
        foreach (SpawnableObjectThroughTextureSO obj in objectDetector.spawnableObjects)
        {
            SpawnableItem item = Instantiate(uiElementPrefab, spawnContent);
            item.SetData(obj.imageIcon, obj.objectName);
            if (i == 0)
            {
                OnClickItem(item);
            }
            item.button.onClick.AddListener(() =>
            {
                OnClickItem(item);
            });
            i++;
        }
    }

    private void OnClickItem(SpawnableItem item)
    {
        if (lastSelectedItem != null)
        {
            lastSelectedItem.SetSelected(false);
        }
        lastSelectedItem = item;
        objectDetector.SetSelectedObject(item.transform.GetSiblingIndex());
        item.SetSelected(true);
    }

}
