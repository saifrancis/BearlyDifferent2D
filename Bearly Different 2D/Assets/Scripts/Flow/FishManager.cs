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

        if (score >= 5)
        {
            scoreText.text = "YOU WIN! Score: " + score;
            CancelInvoke("SpawnLeaf");
            Invoke("GoToNextScene", 5f);
        }
    }

    public void MissedLeaf()
    {
        score--;
        UpdateScoreUI();

        if (score < 0)
        {
            scoreText.text = "GAME OVER! Score: " + score;
            CancelInvoke("SpawnLeaf");
        }
    }

    void UpdateScoreUI()
    {
        scoreText.text = "Score: " + score;
    }

    void GoToNextScene()
    {
        SceneManager.LoadScene("5Page_Five");
    }
}