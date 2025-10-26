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
    public float autoTransitionDelay = 2f;
    public float waitBeforeZoom = 2f;

    [Header("Scene Flow")]
    public string nextSceneName;

    [Header("Choice Panel Settings")]
    public bool useChoicePanel = false;
    public GameObject choiceUI;
    public string choice1Scene;
    public string choice2Scene;
    public string choice3Scene;

    private RectTransform zoomingPanel;
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalSizeDelta;
    private Vector2 originalAnchoredPosition;
    private Vector2 originalPivot;
    private bool isZoomedIn = false;
    private string chosenScene;

    [Header("Glove Control")]
    [Tooltip("When enabled, LEFT/RIGHT poses from AccelerometerPos will navigate panels.")]
    public bool useGloveInput = true;

    // reference to accelerometer script for subscribe and unsubscribe
    private AccelerometerPos accRef;

    void Start()
    {
        foreach (GameObject panel in panels)
        {
            if (panel.TryGetComponent(out CanvasGroup cg))
            {
                cg.alpha = 0f;
            }
            else
            {
                CanvasGroup newCg = panel.AddComponent<CanvasGroup>();
                newCg.alpha = 0f;
            }
        }

        if (choiceUI != null)
            choiceUI.SetActive(false);

        if (useZoomTransition && zoomPanelIndex >= 0 && zoomPanelIndex < panels.Length)
        {
            zoomingPanel = panels[zoomPanelIndex].GetComponent<RectTransform>();
            originalAnchorMin = zoomingPanel.anchorMin;
            originalAnchorMax = zoomingPanel.anchorMax;
            originalSizeDelta = zoomingPanel.sizeDelta;
            originalAnchoredPosition = zoomingPanel.anchoredPosition;
            originalPivot = zoomingPanel.pivot;
        }

        if (panels.Length > 0)
        {
            currentIndex = 0;
            StartCoroutine(FadeInPanel(panels[0]));
        }

        // subscribe to pose events
        if (useGloveInput)
        {
            accRef = FindObjectOfType<AccelerometerPos>();
            if (accRef != null)
            {
                accRef.OnPose += OnAccelPose;
            }
            else
            {
                Debug.LogWarning("No AccelerometerPos found in scene");
            }
        }
    }

    void OnDestroy()
    {
        if (accRef != null)
        {
            accRef.OnPose -= OnAccelPose;
        }
    }

    private void OnAccelPose(string pose)
    {
        if (!useGloveInput) return;
        if (isZoomedIn) return;

        // only act on left and right
        if (pose == "RIGHT")
        {
            ShowNextPanel();
        }
        else if (pose == "LEFT")
        {
            ShowPreviousPanel();
        }
        // DOWN and NEUTRAL do nothing here
    }

    void Update()
    {
        if (!isZoomedIn)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                ShowNextPanel();
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                ShowPreviousPanel();
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Escape) && useZoomTransition)
            {
                StartCoroutine(ZoomOut());
            }
        }
    }

    void ShowNextPanel()
    {
        if (currentIndex < panels.Length - 1)
        {
            currentIndex++;
            StartCoroutine(FadeInPanel(panels[currentIndex])); // keep your fade-in-only logic
            CheckForTransition();
        }
    }

    void ShowPreviousPanel()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
            // keep behaviour consistent: only fade in the newly selected panel
            StartCoroutine(FadeInPanel(panels[currentIndex]));
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
        if (useZoomTransition && currentIndex == zoomPanelIndex)
        {
            if (useChoicePanel && choiceUI != null)
            {
                choiceUI.SetActive(true);
            }
            else
            {
                StartCoroutine(WaitBeforeZoomIn());
            }
        }
        else if (!useZoomTransition && currentIndex == panels.Length - 1)
        {
            StartCoroutine(WaitAndLoadNextScene(autoTransitionDelay));
        }
    }

    IEnumerator WaitBeforeZoomIn()
    {
        yield return new WaitForSeconds(waitBeforeZoom);
        StartCoroutine(ZoomIn(nextSceneName));
    }

    public void ChooseOption(int option)
    {
        if (option == 1) chosenScene = choice1Scene;
        else if (option == 2) chosenScene = choice2Scene;
        else if (option == 3) chosenScene = choice3Scene;

        choiceUI.SetActive(false);
        StartCoroutine(WaitBeforeZoomChoice());
    }

    IEnumerator WaitBeforeZoomChoice()
    {
        yield return new WaitForSeconds(waitBeforeZoom);
        StartCoroutine(ZoomIn(chosenScene));
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

        yield return new WaitForSeconds(2f);

        if (!string.IsNullOrEmpty(targetScene))
        {
            SceneManager.LoadScene(targetScene);
        }
        else
        {
            Debug.LogError("No scene name set! Please assign nextSceneName or choice scene.");
        }
    }

    IEnumerator ZoomOut()
    {
        Vector2 startMin = zoomingPanel.anchorMin;
        Vector2 startMax = zoomingPanel.anchorMax;
        Vector2 startPos = zoomingPanel.anchoredPosition;
        Vector2 startSize = zoomingPanel.sizeDelta;

        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / zoomDuration;

            zoomingPanel.anchorMin = Vector2.Lerp(startMin, originalAnchorMin, t);
            zoomingPanel.anchorMax = Vector2.Lerp(startMax, originalAnchorMax, t);
            zoomingPanel.anchoredPosition = Vector2.Lerp(startPos, originalAnchoredPosition, t);
            zoomingPanel.sizeDelta = Vector2.Lerp(startSize, originalSizeDelta, t);

            yield return null;
        }

        isZoomedIn = false;
    }

    IEnumerator WaitAndLoadNextScene(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
