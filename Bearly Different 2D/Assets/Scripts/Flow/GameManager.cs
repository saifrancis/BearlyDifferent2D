using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public GridManager gridManager;
    public TextMeshProUGUI scoreText;

    private int currentRow = 0;
    private int currentCol = 0;
    private Berry activeBerry;

    private bool isChoosingSwap = false;
    private int successfulMatches = 0;
    public int matchesNeeded = 5;
    private bool levelComplete = false;
    private bool isResolving = false;

    [Header("Outline Colors")]
    public Color navigateColor = new Color32(255, 69, 0, 255); // OrangeRed
    public Color swapReadyColor = Color.yellow;
    public Color matchColor = Color.green;

    [Header("Match Wiggle")]
    public bool enableMatchWiggle = true;
    public float wiggleDuration = 0.20f;
    public float wiggleScale = 1.10f;
    public float wiggleAngle = 10f;

    [Header("Help")]
    [SerializeField] private GameObject helpPanel;     // assign in Inspector
    [SerializeField] private bool helpStartsVisible = false;

    void Start()
    {
        SetActiveBerry(gridManager.grid[currentRow, currentCol]);
        UpdateScoreUI();

        if (helpPanel) helpPanel.SetActive(helpStartsVisible);   // init
        SetActiveBerry(gridManager.grid[currentRow, currentCol]);
        UpdateScoreUI();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
            ToggleHelpPanel();

        if (levelComplete || isResolving) return;

        if (!isChoosingSwap)
        {
            HandleNavigation();
            if (Input.GetKeyDown(KeyCode.Space))
                ArmSwap();
        }
        else
        {
            HandleSwap();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            SceneManager.LoadScene("1Page_One");
    }

    void HandleNavigation()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow) && currentCol < gridManager.cols - 1)
            MoveTo(currentRow, currentCol + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) && currentCol > 0)
            MoveTo(currentRow, currentCol - 1);
        else if (Input.GetKeyDown(KeyCode.UpArrow) && currentRow > 0)
            MoveTo(currentRow - 1, currentCol);
        else if (Input.GetKeyDown(KeyCode.DownArrow) && currentRow < gridManager.rows - 1)
            MoveTo(currentRow + 1, currentCol);
    }

    private void ToggleHelpPanel()
    {
        if (!helpPanel) return;
        bool next = !helpPanel.activeSelf;
        helpPanel.SetActive(next);

        // Optional: pause gameplay while help is open
        // Time.timeScale = next ? 0f : 1f;
    }

    void HandleSwap()
    {
        int targetRow = currentRow;
        int targetCol = currentCol;

        if (Input.GetKeyDown(KeyCode.RightArrow)) targetCol++;
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) targetCol--;
        else if (Input.GetKeyDown(KeyCode.UpArrow)) targetRow--;
        else if (Input.GetKeyDown(KeyCode.DownArrow)) targetRow++;
        else return;

        if (targetRow >= 0 && targetRow < gridManager.rows && targetCol >= 0 && targetCol < gridManager.cols)
        {
            Berry targetBerry = gridManager.grid[targetRow, targetCol];
            gridManager.Swap(activeBerry, targetBerry);
            StartCoroutine(ResolveWithCascades());
        }
    }

    IEnumerator ResolveWithCascades()
    {
        isResolving = true;

        while (true)
        {
            var groups = gridManager.FindMatchGroups();
            if (groups == null || groups.Count == 0) break;

            var flat = gridManager.FlattenUnique(groups);

            // 1) Optional wiggle on each matched berry
            if (enableMatchWiggle)
                yield return StartCoroutine(WiggleGroup(flat, wiggleDuration, wiggleScale, wiggleAngle));

            // 2) Your existing green flash (now uses matchColor from Inspector)
            yield return StartCoroutine(gridManager.FlashMatches(flat, matchColor, 2f));

            // 3) Remove & collapse like before
            gridManager.RemoveAndCollapse(flat);

            successfulMatches += groups.Count;
            UpdateScoreUI();

            if (successfulMatches >= matchesNeeded)
            {
                levelComplete = true;
                yield return new WaitForSeconds(0.5f);
                StartCoroutine(LoadNextSceneAfterDelay(5f));
                break;
            }

            yield return null;
        }

        isChoosingSwap = false;
        SetOutlineColor(activeBerry, navigateColor);
        isResolving = false;
    }

    IEnumerator LoadNextSceneAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene("2Page_Two");
    }

    void MoveTo(int newRow, int newCol)
    {
        if (activeBerry != null)
            SetOutlineColor(activeBerry, Color.clear); 

        currentRow = newRow;
        currentCol = newCol;
        SetActiveBerry(gridManager.grid[currentRow, currentCol]);
    }

    void SetActiveBerry(Berry berry)
    {
        activeBerry = berry;
        SetOutlineColor(activeBerry, navigateColor);
    }

    void ArmSwap()
    {
        if (levelComplete) return;
        if (!isChoosingSwap)
        {
            isChoosingSwap = true;
            SetOutlineColor(activeBerry, swapReadyColor);
        }
    }

    // ✅ Glove Integration Methods (for MiniGame1Glove)
    public void GloveMoveLeft()
    {
        if (levelComplete || isResolving) return;
        if (!isChoosingSwap) TryMove(0, -1);
        else TrySwapDir(0, -1);
    }

    public void GloveMoveRight()
    {
        if (levelComplete || isResolving) return;
        if (!isChoosingSwap) TryMove(0, 1);
        else TrySwapDir(0, 1);
    }

    public void GloveMoveUp()
    {
        if (levelComplete || isResolving) return;
        if (!isChoosingSwap) TryMove(-1, 0);
        else TrySwapDir(-1, 0);
    }

    public void GloveMoveDown()
    {
        if (levelComplete || isResolving) return;
        if (!isChoosingSwap) TryMove(1, 0);
        else TrySwapDir(1, 0);
    }

    public void GloveSelectOrSwapArm()
    {
        if (levelComplete || isResolving) return;
        if (!isChoosingSwap)
        {
            isChoosingSwap = true;
            SetOutlineColor(activeBerry, swapReadyColor);
        }
    }

    private void TryMove(int dr, int dc)
    {
        int nr = currentRow + dr;
        int nc = currentCol + dc;
        if (nr >= 0 && nr < gridManager.rows && nc >= 0 && nc < gridManager.cols)
            MoveTo(nr, nc);
    }

    private void TrySwapDir(int dr, int dc)
    {
        int targetRow = currentRow + dr;
        int targetCol = currentCol + dc;
        if (targetRow < 0 || targetRow >= gridManager.rows || targetCol < 0 || targetCol >= gridManager.cols)
        {
            isChoosingSwap = false;
            SetOutlineColor(activeBerry, navigateColor);
            return;
        }

        Berry targetBerry = gridManager.grid[targetRow, targetCol];
        gridManager.Swap(activeBerry, targetBerry);
        StartCoroutine(ResolveWithCascades());
    }

    // ✅ UI + Outline Helpers
    void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = $"{successfulMatches}/{matchesNeeded}";
    }

    void SetOutlineColor(Berry berry, Color color)
    {
        if (berry == null) return;

        var outline = berry.GetComponent<Outline>();
        if (outline != null)
        {

            outline.enabled = color != Color.clear;
            outline.effectColor = color;
            outline.effectDistance = new Vector2(5f, -5f); // base thickness 
        }
    }


    // Wiggle the entire group briefly
    IEnumerator WiggleGroup(List<Berry> berries, float duration, float scale, float angle)
    {
        // start per-berry wiggles
        foreach (var b in berries)
            if (b != null) StartCoroutine(Wiggle(b.transform, duration, scale, angle));

        yield return new WaitForSeconds(duration);
    }

    // Simple wiggle: oscillate rotation & scale, then restore
    IEnumerator Wiggle(Transform t, float duration, float scale, float angle)
    {
        if (t == null) yield break;

        Vector3 baseScale = t.localScale;
        Quaternion baseRot = t.localRotation;

        float elapsed = 0f;
        float freq = 24f; // wiggle speed

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float s = 1f + (Mathf.Sin(elapsed * freq) * 0.5f + 0.5f) * (scale - 1f); // 1..scale
            float a = Mathf.Sin(elapsed * freq) * angle; // -angle..angle

            t.localScale = baseScale * s;
            t.localRotation = Quaternion.Euler(0f, 0f, a);

            yield return null;
        }

        // restore
        t.localScale = baseScale;
        t.localRotation = baseRot;
    }
}
