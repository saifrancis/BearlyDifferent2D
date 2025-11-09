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
    [SerializeField] private GameObject helpPanel;         
    [SerializeField] private bool helpStartsVisible = true; 

    private int score = 0;
    private int targetScore = 5; 

    public WinText wt;
    public ScoreTextFeedback scoreFeedback;

    [Header("Catch FX")]
    public ParticleSystem catchFXPrefab;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        UpdateScoreUI();

        if (helpPanel != null)
        {
            helpPanel.SetActive(helpStartsVisible);
         
        }

        InvokeRepeating(nameof(SpawnLeaf), 1f, spawnInterval);

    }

    void Update()
    {
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
            rb.gravityScale = Random.Range(0.2f, 0.5f); 
    }

    public void CaughtLeaf(Leaf leaf)
    {
        score++;

        if (leaf != null)
        {
            if (catchFXPrefab != null)
            {
                ParticleSystem fx = Instantiate(catchFXPrefab, leaf.transform.position, Quaternion.identity);
                fx.Play();
                Destroy(fx.gameObject, fx.main.duration + fx.main.startLifetime.constantMax + 0.2f);
            }

            Destroy(leaf.gameObject);
        }

        if (leaf != null) Destroy(leaf.gameObject);
        UpdateScoreUI();

        if (scoreFeedback != null)
            scoreFeedback.Play();

        if (score >= targetScore)
        {
            scoreText.text = "YOU WIN!";

            wt.PlayWin(); 

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
