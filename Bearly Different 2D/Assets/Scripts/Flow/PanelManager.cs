using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class PanelManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject[] panels;
    public float fadeDuration = 1f;
    private int currentIndex = -1;

    [Header("Scene Flow (Auto)")]
    public string nextSceneName;            // Scene to load after last panel (when not in choice/grow modes)
    public float waitBeforeSceneLoad = 2f;  // Delay before scene change (auto mode)
    private bool isLoadingNextScene = false;

    [Header("Choice Mode (Last Panel)")]
    [Tooltip("If enabled, when the last panel is shown the player must press 1, 2, or 3 to pick a scene.")]
    public bool useChoiceOnLastPanel = false;
    public string choice1Scene = "MiniGame_2.1";
    public string choice2Scene = "MiniGame_2.2";
    public string choice3Scene = "MiniGame_2.3";
    private bool waitingForChoice = false;

    [Header("Grow Mode (Last Panel)")]
    [Tooltip("If enabled (and not using Choice Mode), when the last panel is shown all panels will smoothly grow/move to the target position/size, wait, then load the next scene.")]
    public bool useGrowOnLastPanel = false;

    [Tooltip("Optional panel to deactivate right when the grow animation starts.")]
    public GameObject panelToDeactivateOnGrow;

    [Tooltip("Seconds for the grow/move animation.")]
    public float growDuration = 1.75f;

    [Tooltip("Seconds to wait after the grow finishes before loading the scene.")]
    public float growWaitAfter = 10f;

    [Tooltip("Target anchored position for each panel's RectTransform.")]
    public Vector2 growTargetAnchoredPos = Vector2.zero;

    [Tooltip("Target sizeDelta (Width x Height) for each panel's RectTransform.")]
    public Vector2 growTargetSize = new Vector2(252.1366f, 356.5933f);

    [Header("Glove Control")]
    public bool useGloveInput = true;
    private UnifiedGloveController accRef;

    [Header("Toggle Panel (press key to show/hide)")]
    public GameObject togglePanel;
    public KeyCode toggleKey = KeyCode.H;
    public float toggleFadeDuration = 0.2f;  // quick fade
    private bool togglePanelActive = false;
    private Coroutine toggleCoroutine;

    void Start()
    {
        // Ensure all panels start hidden (alpha 0)
        foreach (GameObject panel in panels)
        {
            if (panel == null) continue;
            if (!panel.TryGetComponent(out CanvasGroup cg))
                cg = panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
        }

        // Default toggle panel (fallback to first panel if none assigned)
        if (togglePanel == null && panels != null && panels.Length > 0)
            togglePanel = panels[0];

        if (togglePanel != null)
            togglePanelActive = togglePanel.activeSelf;

        currentIndex = -1;

        // Optional glove input
        if (useGloveInput)
        {
            accRef = FindObjectOfType<UnifiedGloveController>();
            if (accRef != null)
            {
                accRef.OnPose += OnAccelPose;
                accRef.OnChoice += OnAccelChoice;
            }
            else
            {
                Debug.LogWarning("UnifiedGloveController not found for PanelManager");
            }
        }
    }

    void OnDestroy()
    {
        if (accRef != null)
        {
            accRef.OnPose -= OnAccelPose;
            accRef.OnChoice -= OnAccelChoice;
        }
    }

    private void OnAccelPose(string pose)
    {
        if (!useGloveInput) return;
        if (waitingForChoice) return; // in choice mode, ignore RIGHT to prevent skipping
        if (pose == "RIGHT")
            ShowNextPanel();
    }

    private void OnAccelChoice(int n)
    {
        if (!useGloveInput) return;
        if (!waitingForChoice) return; // only accept choices at last panel when choice mode is on

        if (n == 1) LoadSceneSafe(choice1Scene);
        else if (n == 2) LoadSceneSafe(choice2Scene);
        else if (n == 3) LoadSceneSafe(choice3Scene);
    }

    void Update()
    {
        // Toggle a panel with key (no pause logic)
        if (Input.GetKeyDown(toggleKey))
            TogglePanel();

        // If we're waiting for a choice on the last panel, read 1/2/3
        if (waitingForChoice)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) LoadSceneSafe(choice1Scene);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) LoadSceneSafe(choice2Scene);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) LoadSceneSafe(choice3Scene);
            return; // block advancing while awaiting choice
        }

        // Keyboard advance between panels
        if (Input.GetKeyDown(KeyCode.RightArrow))
            ShowNextPanel();
    }

    void TogglePanel()
    {
        if (togglePanel == null) return;

        togglePanelActive = !togglePanelActive;

        if (toggleCoroutine != null)
            StopCoroutine(toggleCoroutine);

        toggleCoroutine = StartCoroutine(FadeTogglePanel(togglePanelActive));
    }

    IEnumerator FadeTogglePanel(bool show)
    {
        CanvasGroup cg = togglePanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = togglePanel.AddComponent<CanvasGroup>();

        if (show)
        {
            togglePanel.SetActive(true);
            float elapsed = 0f;
            while (elapsed < toggleFadeDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(0f, 1f, elapsed / toggleFadeDuration);
                yield return null;
            }
            cg.alpha = 1f;
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < toggleFadeDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(1f, 0f, elapsed / toggleFadeDuration);
                yield return null;
            }
            cg.alpha = 0f;
            togglePanel.SetActive(false);
        }
    }

    void ShowNextPanel()
    {
        if (isLoadingNextScene) return; // prevent double triggers
        if (waitingForChoice) return;   // already at last panel in choice mode

        if (currentIndex < panels.Length - 1)
        {
            currentIndex++;
            StartCoroutine(FadeInPanel(panels[currentIndex]));

            // If this is the last panel…
            if (currentIndex == panels.Length - 1)
            {
                if (useChoiceOnLastPanel)
                {
                    waitingForChoice = true;
                }
                else if (useGrowOnLastPanel)
                {
                    StartCoroutine(GrowAllPanelsThenLoad());
                }
                else
                {
                    StartCoroutine(LoadNextSceneAfterDelay());
                }
            }
        }
    }

    IEnumerator FadeInPanel(GameObject panel)
    {
        if (panel == null) yield break;

        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
            yield return null;
        }
        cg.alpha = 1f;
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
        isLoadingNextScene = true;
        yield return new WaitForSeconds(waitBeforeSceneLoad);
        LoadSceneSafe(nextSceneName);
    }

    private void LoadSceneSafe(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("PanelManager: scene name is empty; no scene loaded.");
            return;
        }

        isLoadingNextScene = true;
        waitingForChoice = false;

        SceneManager.LoadScene(sceneName);
    }

    // --- New grow coroutine ---
    private IEnumerator GrowAllPanelsThenLoad()
    {
        if (isLoadingNextScene) yield break;
        isLoadingNextScene = true; // lock flow while we animate

        // 🔹 NEW LINE: deactivate a specific panel if assigned
        if (panelToDeactivateOnGrow != null)
            panelToDeactivateOnGrow.SetActive(false);

        // Cache initial RT data
        var rts = new RectTransform[panels.Length];
        var startPos = new Vector2[panels.Length];
        var startSize = new Vector2[panels.Length];

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null) continue;

            var rt = panels[i].GetComponent<RectTransform>();
            if (rt == null)
            {
                Debug.LogWarning($"PanelManager: Panel '{panels[i].name}' has no RectTransform.");
                continue;
            }

            rts[i] = rt;
            startPos[i] = rt.anchoredPosition;
            startSize[i] = rt.sizeDelta;
        }

        // Animate all panels towards the target
        float elapsed = 0f;
        while (elapsed < growDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growDuration);

            for (int i = 0; i < rts.Length; i++)
            {
                if (rts[i] == null) continue;

                rts[i].anchoredPosition = Vector2.Lerp(startPos[i], growTargetAnchoredPos, t);
                rts[i].sizeDelta = Vector2.Lerp(startSize[i], growTargetSize, t);
            }

            yield return null;
        }

        // Snap to final
        for (int i = 0; i < rts.Length; i++)
        {
            if (rts[i] == null) continue;
            rts[i].anchoredPosition = growTargetAnchoredPos;
            rts[i].sizeDelta = growTargetSize;
        }

        // Wait, then load
        yield return new WaitForSeconds(growWaitAfter);
        LoadSceneSafe(nextSceneName);
    }
}
