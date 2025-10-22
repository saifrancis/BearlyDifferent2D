using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class PanelManager : MonoBehaviour
{
    public GameObject[] panels;
    public Color activeColor = Color.blue;
    public Color inactiveColor = Color.white;

    private int currentIndex = 0;
    private bool isZoomedIn = false;

    private RectTransform zoomingPanel;
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalSizeDelta;
    private Vector2 originalAnchoredPosition;
    private Vector2 originalPivot;

    [Header("Transition Settings")]
    public bool useZoomTransition = true;
    public int zoomPanelIndex = 3;
    public float zoomDuration = 1f;
    public float autoTransitionDelay = 2f;

    [Header("Scene Flow")]
    public string nextSceneName;

    [Header("Choice Panel Settings")]
    public bool useChoicePanel = false;
    public GameObject choiceUI;
    public string choice1Scene;
    public string choice2Scene;
    public string choice3Scene;

    private string chosenScene;

    void Start()
    {
        if (useZoomTransition && zoomPanelIndex >= 0 && zoomPanelIndex < panels.Length)
        {
            zoomingPanel = panels[zoomPanelIndex].GetComponent<RectTransform>();
            originalAnchorMin = zoomingPanel.anchorMin;
            originalAnchorMax = zoomingPanel.anchorMax;
            originalSizeDelta = zoomingPanel.sizeDelta;
            originalAnchoredPosition = zoomingPanel.anchoredPosition;
            originalPivot = zoomingPanel.pivot;
        }

        if (choiceUI != null)
            choiceUI.SetActive(false);

        UpdatePanels();
    }

    void Update()
    {
        if (!isZoomedIn)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                currentIndex = (currentIndex + 1) % panels.Length;
                CheckForTransition();
                UpdatePanels();
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                currentIndex = (currentIndex - 1 + panels.Length) % panels.Length;
                CheckForTransition();
                UpdatePanels();
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

    void UpdatePanels()
    {
        for (int i = 0; i < panels.Length; i++)
        {
            Image img = panels[i].GetComponent<Image>();
            img.color = (i == currentIndex) ? activeColor : inactiveColor;
        }
    }

    void CheckForTransition()
    {
        if (useZoomTransition)
        {
            if (currentIndex == zoomPanelIndex)
            {
                if (useChoicePanel && choiceUI != null)
                {
                    choiceUI.SetActive(true);
                }
                else
                {
                    StartCoroutine(ZoomIn(nextSceneName));
                }
            }
        }
        else
        {
            if (currentIndex == panels.Length - 1)
            {
                StartCoroutine(WaitAndLoadNextScene(autoTransitionDelay));
            }
        }
    }

    public void ChooseOption(int option)
    {
        if (option == 1) chosenScene = choice1Scene;
        else if (option == 2) chosenScene = choice2Scene;
        else if (option == 3) chosenScene = choice3Scene;

        choiceUI.SetActive(false);
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

        zoomingPanel.anchorMin = targetMin;
        zoomingPanel.anchorMax = targetMax;
        zoomingPanel.anchoredPosition = targetPos;
        zoomingPanel.sizeDelta = targetSize;

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

        zoomingPanel.anchorMin = originalAnchorMin;
        zoomingPanel.anchorMax = originalAnchorMax;
        zoomingPanel.anchoredPosition = originalAnchoredPosition;
        zoomingPanel.sizeDelta = originalSizeDelta;

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
