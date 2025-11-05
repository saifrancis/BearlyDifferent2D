using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class FishManager : MonoBehaviour
{
    public static FishManager Instance;

    [Header("Spawning")]
    public GameObject leafPrefab;
    public Transform[] spawnZones;
    public float spawnInterval = 2f;

    [Header("UI")]
    public TextMeshProUGUI scoreText;

    [Header("Help Panel")]
    [SerializeField] private GameObject helpPanel;          // Assign in Inspector (full-screen panel or any UI)
    [SerializeField] private bool helpStartsVisible = true; // Starts active

    private int score = 0;
    private int targetScore = 5; // Number of leaves needed to win

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Init UI
        UpdateScoreUI();

        // Init help panel (no pause)
        if (helpPanel != null)
            helpPanel.SetActive(helpStartsVisible);

        // Start spawning leaves
        InvokeRepeating(nameof(SpawnLeaf), 1f, spawnInterval);
    }

    void Update()
    {
        // Toggle help on H (no pause)
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }
    }

    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;
        helpPanel.SetActive(!helpPanel.activeSelf);
    }

    void SpawnLeaf()
    {
        if (leafPrefab == null || spawnZones == null || spawnZones.Length == 0) return;

        int zoneIndex = Random.Range(0, spawnZones.Length);
        Transform spawnPoint = spawnZones[zoneIndex];

        GameObject leaf = Instantiate(leafPrefab, spawnPoint.position, Quaternion.identity);
        var rb = leaf.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.gravityScale = Random.Range(0.2f, 0.5f); // random fall speed
    }

    public void CaughtLeaf(Leaf leaf)
    {
        score++;
        if (leaf != null) Destroy(leaf.gameObject);
        UpdateScoreUI();

        // Check win condition
        if (score >= targetScore)
        {
            scoreText.text = "YOU WIN!";
            CancelInvoke(nameof(SpawnLeaf));
            Invoke(nameof(GoToNextScene), 5f);
        }
    }

    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score:\n" + score;
    }

    void GoToNextScene()
    {
        SceneManager.LoadScene("5Page_Five");
    }
}
