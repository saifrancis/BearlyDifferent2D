using UnityEngine;

public class Leaf : MonoBehaviour
{
    private bool inCatchZone = false;

    void Update()
    {
        // If leaf falls off screen, just destroy it
        if (transform.position.y < -6f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("CatchZone"))
        {
            inCatchZone = true;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("CatchZone"))
        {
            inCatchZone = false;
        }
    }

    public bool IsInCatchZone()
    {
        return inCatchZone;
    }
}
