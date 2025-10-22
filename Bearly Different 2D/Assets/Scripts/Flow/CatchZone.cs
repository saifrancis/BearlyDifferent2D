using UnityEngine;

public class CatchZone : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Leaf[] leaves = FindObjectsOfType<Leaf>();
            foreach (Leaf leaf in leaves)
            {
                if (leaf.IsInCatchZone())
                {
                    FishManager.Instance.CaughtLeaf(leaf);
                    return;
                }
            }
        }
    }
}
