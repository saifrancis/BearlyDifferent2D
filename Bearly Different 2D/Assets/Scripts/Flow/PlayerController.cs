using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Transform[] zones;
    private int currentZone = 0; // start at zone 1 (index 0)

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
}

