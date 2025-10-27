using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class FishManager : MonoBehaviour
{
    public static FishManager Instance;

    public GameObject leafPrefab;
    public Transform[] spawnZones;
    public float spawnInterval = 2f;

    public TextMeshProUGUI scoreText;

    private int score = 0;
    private int targetScore = 5; // Number of leaves needed to win

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        UpdateScoreUI();
        InvokeRepeating("SpawnLeaf", 1f, spawnInterval);
    }

    void SpawnLeaf()
    {
        int zoneIndex = Random.Range(0, spawnZones.Length);
        Transform spawnPoint = spawnZones[zoneIndex];

        GameObject leaf = Instantiate(leafPrefab, spawnPoint.position, Quaternion.identity);
        Rigidbody2D rb = leaf.GetComponent<Rigidbody2D>();
        rb.gravityScale = Random.Range(0.2f, 0.5f); // random fall speed
    }

    public void CaughtLeaf(Leaf leaf)
    {
        score++;
        Destroy(leaf.gameObject);
        UpdateScoreUI();

        // Check win condition
        if (score >= targetScore)
        {
            scoreText.text = "YOU WIN! Score: " + score;
            CancelInvoke("SpawnLeaf");
            Invoke("GoToNextScene", 5f);
        }
    }

    // Removed MissedLeaf completely since no penalty is needed

    void UpdateScoreUI()
    {
        scoreText.text = "Score: " + score;
    }

    void GoToNextScene()
    {
        SceneManager.LoadScene("5Page_Five");
    }
}
