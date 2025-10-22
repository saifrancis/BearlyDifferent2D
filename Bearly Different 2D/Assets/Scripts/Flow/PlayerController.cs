using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Transform[] zones;
    private int currentZone = 1;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            currentZone = Mathf.Max(0, currentZone - 1);
            MoveToZone();
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            currentZone = Mathf.Min(zones.Length - 1, currentZone + 1);
            MoveToZone();
        }
    }

    void MoveToZone()
    {
        transform.position = new Vector3(zones[currentZone].position.x, transform.position.y, 0);
    }
}
