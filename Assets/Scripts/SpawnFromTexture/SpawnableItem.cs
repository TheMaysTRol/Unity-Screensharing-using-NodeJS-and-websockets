using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpawnableItem : MonoBehaviour
{

    [SerializeField] private GameObject selected;
    private bool isSelected;

    public Button button;
    public Image icon;
    public TextMeshProUGUI textName;

    public void SetSelected(bool selected)
    {
        this.selected.gameObject.SetActive(selected);
        this.isSelected = selected;
    }

    public void SetData(Sprite icon, string name)
    {
        this.icon.sprite = icon;
        this.textName.text = name;
    }

    public bool IsSelected()
    {
        return isSelected;
    }
}
