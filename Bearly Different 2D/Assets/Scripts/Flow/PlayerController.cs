using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Transform[] zones;
    private int currentZone = 0; 
    public SpriteRenderer boSprite;
    public Sprite normalSprite;
    public Sprite biteFlashSprite;
    public float flashDuration = 0.12f;

    private Coroutine flashRoutine;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) { currentZone = 0; MoveToZone(); }
        else if (Input.GetKeyDown(KeyCode.Alpha2)) { currentZone = 1; MoveToZone(); }
        else if (Input.GetKeyDown(KeyCode.Alpha3)) { currentZone = 2; MoveToZone(); }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryCatchLeaf();
            TriggerBiteFlash();
        }
    }

    public void OnShake()
    {
        TryCatchLeaf();
        TriggerBiteFlash();
    }

    void MoveToZone()
    {
        if (zones == null || zones.Length == 0) return;
        currentZone = Mathf.Clamp(currentZone, 0, zones.Length - 1);
        transform.position = new Vector3(zones[currentZone].position.x, transform.position.y, 0);
    }

    void TryCatchLeaf()
    {
        if (zones == null || zones.Length == 0) return;
        currentZone = Mathf.Clamp(currentZone, 0, zones.Length - 1);

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
        if (boSprite == null || biteFlashSprite == null || normalSprite == null) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(BiteFlashCo());
    }

    private IEnumerator BiteFlashCo()
    {
        boSprite.sprite = biteFlashSprite;
        yield return new WaitForSeconds(flashDuration);
        boSprite.sprite = normalSprite;
    }
}
