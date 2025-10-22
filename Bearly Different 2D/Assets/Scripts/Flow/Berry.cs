using UnityEngine;
using UnityEngine.UI;

public class Berry : MonoBehaviour
{
    public int row;
    public int col;
    public int type;
    private Image image;
    private Outline outline;

    void Awake()
    {
        image = GetComponent<Image>();
        outline = GetComponent<Outline>();
        outline.enabled = false;
    }

    public void SetType(int newType, Sprite sprite)
    {
        type = newType;
        image.sprite = sprite;
    }

    public void Highlight(bool on)
    {
        outline.enabled = on;
    }
}