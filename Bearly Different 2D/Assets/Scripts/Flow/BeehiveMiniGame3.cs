using UnityEngine;

public class BeehiveMiniGame3 : MonoBehaviour
{
    [Header("Spawner Settings")]
    public Transform spawner;
    public GameObject[] rockPrefabs;
    public float spawnXRange = 1f;

    public bool canSpawn = true;

    [Header("Help Panel")]
    [SerializeField] private GameObject helpPanel;      // Assign your help panel in the Inspector
    [SerializeField] private bool helpStartsVisible = true;


    private bool HelpVisible => helpPanel != null && helpPanel.activeSelf;

    void Start()
    {
        // Initialize the help panel
        if (helpPanel != null)
            helpPanel.SetActive(helpStartsVisible);
    }

    void Update()
    {
        // --- Handle help toggle ---
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }

        if (helpPanel.activeInHierarchy == true) return;

        // --- Gameplay logic ---
        if (canSpawn && Input.GetKeyDown(KeyCode.Space))
        {
            SpawnRock();
        }
    }

    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;

        bool next = !helpPanel.activeSelf;
        helpPanel.SetActive(next);
    }

    void SpawnRock()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("No rock prefabs assigned!");
            return;
        }

        int index = Random.Range(0, rockPrefabs.Length);
        GameObject rockPrefab = rockPrefabs[index];

        float xOffset = Random.Range(-spawnXRange, spawnXRange);
        Vector3 spawnPos = spawner.position + new Vector3(xOffset, 0f, 0f);

        Instantiate(rockPrefab, spawnPos, Quaternion.identity);
    }
}
