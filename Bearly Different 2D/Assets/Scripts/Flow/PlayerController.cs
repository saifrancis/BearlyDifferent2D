using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Transform[] zones;
    private int currentZone = 0; // start at zone 1 (index 0)
    public SpriteRenderer boSprite;
    public Sprite normalSprite;
    public Sprite biteFlashSprite;
    public float flashDuration = 0.12f;

    private Coroutine flashRoutine;

    void Update()
    {
        // Check for number key presses
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            currentZone = 0;
            MoveToZone();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            currentZone = 1;
            MoveToZone();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            currentZone = 2;
            MoveToZone();
        }

        // Catch leaves with spacebar
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryCatchLeaf();
            TriggerBiteFlash();
        }
    }

    void MoveToZone()
    {
        transform.position = new Vector3(zones[currentZone].position.x, transform.position.y, 0);
    }

    void TryCatchLeaf()
    {
        // Cast a small overlap box or circle to check for leaves in the current zone
        Collider2D[] hits = Physics2D.OverlapCircleAll(zones[currentZone].position, 0.5f);
        foreach (Collider2D hit in hits)
        {
            Leaf leaf = hit.GetComponent<Leaf>();
            if (leaf != null)
            {
                FishManager.Instance.CaughtLeaf(leaf);
            }
        }
    }

    private void TriggerBiteFlash()
    {
        if (boSprite == null || biteFlashSprite == null) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(BiteFlashCo());
    }

    private IEnumerator BiteFlashCo()
    {
        boSprite.sprite = biteFlashSprite;
        yield return new WaitForSeconds(flashDuration);
        boSprite.sprite = normalSprite;
    }
}

