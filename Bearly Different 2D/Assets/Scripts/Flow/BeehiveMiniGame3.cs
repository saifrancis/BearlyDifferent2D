using UnityEngine;

public class BeehiveMiniGame3 : MonoBehaviour
{
    [Header("Spawner Settings")]
    public Transform spawner;
    public GameObject[] rockPrefabs;
    public float spawnXRange = 1f;

    public bool canSpawn = true;

    void Update()
    {
        if (canSpawn && Input.GetKeyDown(KeyCode.Space))
        {
            SpawnRock();
        }
    }

    void SpawnRock()
    {
        int index = Random.Range(0, rockPrefabs.Length);
        GameObject rockPrefab = rockPrefabs[index];

        float xOffset = Random.Range(-spawnXRange, spawnXRange);

        Vector3 spawnPos = spawner.position + new Vector3(xOffset, 0f, 0f);

        Instantiate(rockPrefab, spawnPos, Quaternion.identity);
    }
}
