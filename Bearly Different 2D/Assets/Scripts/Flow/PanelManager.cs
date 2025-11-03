
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

    [Header("Zoom Transition Settings")]
    public bool useZoomTransition = true;
    public int zoomPanelIndex = 3;
    public float zoomDuration = 1f;
    public float waitBeforeSceneLoad = 2f;

    [Header("Scene Flow")]
    public string nextSceneName;

    private RectTransform zoomingPanel;
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalSizeDelta;
    private Vector2 originalAnchoredPosition;
    private bool isZoomedIn = false;
    private bool zoomPanelReached = false;

    [Header("Glove Control")]
    public bool useGloveInput = true;

    private AccelerometerPos accRef;

    private bool isChoiceScene = false;

    [Header("Toggle Panel (press key to show/hide)")]
    public GameObject togglePanel;
    public KeyCode toggleKey = KeyCode.H;
    public float toggleFadeDuration = 0.2f;  // quick fade
    private bool togglePanelActive = false;
    private Coroutine toggleCoroutine;

    void Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        isChoiceScene = sceneName == "3Page_Three";

        foreach (GameObject panel in panels)
        {
            if (panel.TryGetComponent(out CanvasGroup cg))
                cg.alpha = 0f;
            else
                panel.AddComponent<CanvasGroup>().alpha = 0f;
        }

        if (togglePanel == null && panels != null && panels.Length > 0)
        {
            togglePanel = panels[0];
        }

        if (togglePanel != null)
            togglePanelActive = togglePanel.activeSelf;

        if (useZoomTransition && zoomPanelIndex >= 0 && zoomPanelIndex < panels.Length)
        {
            zoomingPanel = panels[zoomPanelIndex].GetComponent<RectTransform>();
            originalAnchorMin = zoomingPanel.anchorMin;
            originalAnchorMax = zoomingPanel.anchorMax;
            originalSizeDelta = zoomingPanel.sizeDelta;
            originalAnchoredPosition = zoomingPanel.anchoredPosition;
        }

        currentIndex = -1;

        if (useGloveInput)
        {
            accRef = FindObjectOfType<AccelerometerPos>();
            if (accRef != null)
            {
                accRef.OnPose += OnAccelPose;
                accRef.OnChoice += OnAccelChoice;

                // Map finger patterns to choices on the choice scene
                accRef.OnChoice += n =>
                {
                    if (!isChoiceScene) return;
                    if (!zoomPanelReached) return;
                    if (isZoomedIn) return;

                    if (n == 1) StartCoroutine(ZoomIn("MiniGame_2.1"));
                    else if (n == 2) StartCoroutine(ZoomIn("MiniGame_2.2"));
                    else if (n == 3) StartCoroutine(ZoomIn("MiniGame_2.3"));
                };
            }
            else
            {
                Debug.LogWarning("AccelerometerPos not found for PanelManager");
            }
        }


    }

    void OnDestroy()
    {
        if (accRef != null)
            accRef.OnPose -= OnAccelPose;
        accRef.OnChoice -= OnAccelChoice;
    }

    private void OnAccelPose(string pose)
    {
        if (!useGloveInput || isZoomedIn) return;
        if (pose == "RIGHT")
            ShowNextPanel();
    }

    private void OnAccelChoice(int n)
    {
        if (!useGloveInput) return;
        if (!isChoiceScene) return;
        if (!zoomPanelReached) return;
        if (isZoomedIn) return;

        if (n == 1) StartCoroutine(ZoomIn("MiniGame_2.1"));
        else if (n == 2) StartCoroutine(ZoomIn("MiniGame_2.2"));
        else if (n == 3) StartCoroutine(ZoomIn("MiniGame_2.3"));
    }


    void Update()
    {
        // Toggle panel with key
        if (Input.GetKeyDown(toggleKey))
        {
            TogglePanel();
        }

        if (!isZoomedIn)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
                ShowNextPanel();

            if (isChoiceScene && zoomPanelReached)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    StartCoroutine(ZoomIn("MiniGame_2.1"));
                else if (Input.GetKeyDown(KeyCode.Alpha2))
                    StartCoroutine(ZoomIn("MiniGame_2.2"));
                else if (Input.GetKeyDown(KeyCode.Alpha3))
                    StartCoroutine(ZoomIn("MiniGame_2.3"));
            }
        }
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
        if (zoomPanelReached)
        {
            if (!isChoiceScene && useZoomTransition)
                StartCoroutine(ZoomIn(nextSceneName));
            return;
        }

        if (currentIndex < panels.Length - 1)
        {
            currentIndex++;
            StartCoroutine(FadeInPanel(panels[currentIndex]));
            CheckForTransition();
        }
    }

    IEnumerator FadeInPanel(GameObject panel)
    {
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

    void CheckForTransition()
    {
        if (currentIndex == panels.Length - 1)
        {
            zoomPanelReached = true;
            if (isChoiceScene) return;
        }
    }

    IEnumerator ZoomIn(string targetScene)
    {
        isZoomedIn = true;

        Vector2 startMin = zoomingPanel.anchorMin;
        Vector2 startMax = zoomingPanel.anchorMax;
        Vector2 startPos = zoomingPanel.anchoredPosition;
        Vector2 startSize = zoomingPanel.sizeDelta;

        Vector2 targetMin = Vector2.zero;
        Vector2 targetMax = Vector2.one;
        Vector2 targetPos = Vector2.zero;
        Vector2 targetSize = Vector2.zero;

        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / zoomDuration;

            zoomingPanel.anchorMin = Vector2.Lerp(startMin, targetMin, t);
            zoomingPanel.anchorMax = Vector2.Lerp(startMax, targetMax, t);
            zoomingPanel.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            zoomingPanel.sizeDelta = Vector2.Lerp(startSize, targetSize, t);

            yield return null;
        }

        yield return new WaitForSeconds(waitBeforeSceneLoad);

        if (!string.IsNullOrEmpty(targetScene))
            SceneManager.LoadScene(targetScene);
    }
}

