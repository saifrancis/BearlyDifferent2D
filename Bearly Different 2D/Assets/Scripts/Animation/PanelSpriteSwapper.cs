using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PanelSpriteSwapper : MonoBehaviour
{
    [Header("Sprite Swap Settings")]
    public Sprite newSprite;          // The sprite to swap to
    public float delay = 1f;          // Delay before swapping

    private bool swapped = false;

    void OnEnable()
    {
        if (!swapped) StartCoroutine(SwapAfterDelay());
    }

    IEnumerator SwapAfterDelay()
    {
        yield return new WaitForSeconds(delay);

        var img = GetComponent<Image>();
        if (img != null)
        {
            img.sprite = newSprite;
            swapped = true;
            yield break;
        }

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sprite = newSprite;
            swapped = true;
        }
    }
}
