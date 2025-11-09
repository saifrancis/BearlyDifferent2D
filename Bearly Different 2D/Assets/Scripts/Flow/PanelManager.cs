using UnityEngine;
using System;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class PanelManager : MonoBehaviour
{
    public GameObject[] panels;
    public float fadeDuration = 1f;
    private int currentIndex = -1;

    public string nextSceneName;            
    public float waitBeforeSceneLoad = 2f;  
    private bool isLoadingNextScene = false;

    public bool useChoiceOnLastPanel = false;
    public string choice1Scene = "MiniGame_2.1";
    public string choice2Scene = "MiniGame_2.2";
    public string choice3Scene = "MiniGame_2.3";
    private bool waitingForChoice = false;

    public bool useGrowOnLastPanel = false;
    public GameObject panelToDeactivateOnGrow;
    public float growDuration = 1.75f;
    public float growWaitAfter = 10f;
    public Vector2 growTargetAnchoredPos = Vector2.zero;
    public Vector2 growTargetSize = new Vector2(252.1366f, 356.5933f);

    public AudioClip[] panelVoiceClips;
    [Range(0f, 1f)] public float voiceVolume = 1f; 
    public AudioSource voiceSource;
    public string homeSceneName = "0HomePage";
    public bool showVoiceOptInOnHome = true;
    public GameObject voiceOptInPanel;
    public Toggle voiceAssistToggleUI;

    private const string PREF_VOICE_ENABLED = "VOICE_ASSIST_ENABLED";
    private bool voiceAssistEnabled = false;  

    public bool useGloveInput = true;
    private UnifiedGloveController accRef;

    public GameObject togglePanel;
    public KeyCode toggleKey = KeyCode.H;
    public float toggleFadeDuration = 0.2f;  
    private bool togglePanelActive = false;
    private Coroutine toggleCoroutine;

    public bool transitionFromRight = true; // forward page turn default
    public int transitionFps = 24;

    [Serializable]
    public struct DeactivateAfterSteps
    {
        public GameObject obj;
        public int steps;
        [HideInInspector] public int startIndex;
    }

    [Header("Selective Deactivate When Advancing")]
    [Tooltip("Only panels in this list will be deactivated when you advance to the next one.")]
    public GameObject[] deactivateOnAdvance;
    public DeactivateAfterSteps[] deactivateAfterSteps;

    public bool fadeOutOnDeactivate = true;
    public float deactivateFadeDuration = 0.2f;

    void Awake()
    {
        foreach (GameObject panel in panels)
        {
            if (panel == null) continue;
            if (!panel.TryGetComponent(out CanvasGroup cg))
                cg = panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
        }

        if (togglePanel == null && panels != null && panels.Length > 0)
            togglePanel = panels[0];

        if (togglePanel != null)
            togglePanelActive = togglePanel.activeSelf;

        currentIndex = -1;

        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = false;       
            voiceSource.spatialBlend = 0f;  
            voiceSource.volume = voiceVolume;
        }

        voiceAssistEnabled = PlayerPrefs.GetInt(PREF_VOICE_ENABLED, 0) == 1;

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

    void Start()
    {
        if (SceneManager.GetActiveScene().name == homeSceneName && showVoiceOptInOnHome)
        {
            if (voiceOptInPanel != null)
                voiceOptInPanel.SetActive(true);

            if (voiceAssistToggleUI != null)
            {
                voiceAssistToggleUI.isOn = voiceAssistEnabled;
           
                voiceAssistToggleUI.onValueChanged.AddListener(OnVoiceAssistToggled);
            }
        }
        else
        {
            if (!voiceAssistEnabled) StopVoice();
        }
    }

    void OnDestroy()
    {
        if (accRef != null)
        {
            accRef.OnPose -= OnAccelPose;
            accRef.OnChoice -= OnAccelChoice;
        }
        if (voiceAssistToggleUI != null)
            voiceAssistToggleUI.onValueChanged.RemoveListener(OnVoiceAssistToggled);
    }

    public void OnVoiceAssistToggled(bool enabled)
    {
        voiceAssistEnabled = enabled;
        PlayerPrefs.SetInt(PREF_VOICE_ENABLED, voiceAssistEnabled ? 1 : 0);
        PlayerPrefs.Save();

        if (!voiceAssistEnabled)
            StopVoice(); 
    }

    public void CloseVoiceOptInPanel()
    {
        if (voiceOptInPanel != null)
            voiceOptInPanel.SetActive(false);
    }


    private void OnAccelPose(string pose)
    {
        if (!useGloveInput) return;
        if (waitingForChoice) return; 
        if (pose == "RIGHT")
            ShowNextPanel();
    }

    private void OnAccelChoice(int n)
    {
        if (!useGloveInput) return;
        if (!waitingForChoice) return; 

        if (n == 1) LoadSceneSafe(choice1Scene);
        else if (n == 2) LoadSceneSafe(choice2Scene);
        else if (n == 3) LoadSceneSafe(choice3Scene);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            TogglePanel();

        if (waitingForChoice)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) LoadSceneSafe(choice1Scene);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) LoadSceneSafe(choice2Scene);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) LoadSceneSafe(choice3Scene);
            return; 
        }

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

    public void ShowNextPanel()
    {
        if (isLoadingNextScene) return; 
        if (waitingForChoice) return;  

        if (currentIndex < panels.Length - 1)
        {
            currentIndex++;
            StartCoroutine(FadeInPanel(panels[currentIndex]));

            if (voiceAssistEnabled)
                PlayVoiceFor(currentIndex);
            else
                StopVoice();

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

            if (currentIndex > 0)
            {
                var oldMover = panels[currentIndex - 1].GetComponent<CharacterMover>();
                if (oldMover) oldMover.isEnabled = false;
            }

            var newMover = panels[currentIndex].GetComponent<CharacterMover>();
            if (newMover)
            {
                newMover.isEnabled = true;
                newMover.StartMoving();
            }
        }

        if (currentIndex > 0)
        {
            CheckPanelsToDeactivate();
        }
    }

    private void PlayVoiceFor(int index)
    {
        if (voiceSource == null) return;

        if (voiceSource.isPlaying) voiceSource.Stop();

        if (panelVoiceClips == null || index < 0 || index >= panelVoiceClips.Length) return;
        var clip = panelVoiceClips[index];
        if (clip == null) return;

        voiceSource.volume = voiceVolume;
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    private void StopVoice()
    {
        if (voiceSource != null && voiceSource.isPlaying)
            voiceSource.Stop();
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

        StopVoice();

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

        StopVoice();

        TransitionLoader.Go(sceneName, fromRight: transitionFromRight, fps: transitionFps);
    }

    private IEnumerator GrowAllPanelsThenLoad()
    {
        if (isLoadingNextScene) yield break;
        isLoadingNextScene = true;

        StopVoice();

        if (panelToDeactivateOnGrow != null)
            panelToDeactivateOnGrow.SetActive(false);

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

        for (int i = 0; i < rts.Length; i++)
        {
            if (rts[i] == null) continue;
            rts[i].anchoredPosition = growTargetAnchoredPos;
            rts[i].sizeDelta = growTargetSize;
        }

        yield return new WaitForSeconds(growWaitAfter);
        LoadSceneSafe(nextSceneName);
    }


    void CheckPanelsToDeactivate()
    {
        foreach (DeactivateAfterSteps item in deactivateAfterSteps)
        {
            GameObject obj = item.obj;
            if (obj != null)
                if (obj.activeInHierarchy)
                {
                    if (currentIndex - item.startIndex >= item.steps)
                        if (fadeOutOnDeactivate) StartCoroutine(FadeOutAndDeactivate(obj));
                        else obj.SetActive(false);
                }
        }
    }

    bool ShouldDeactivate(GameObject panel)
    {
        if (panel == null || deactivateOnAdvance == null) return false;
        return Array.IndexOf(deactivateOnAdvance, panel) >= 0;
    }

    IEnumerator FadeOutAndDeactivate(GameObject panel)
    {
        if (panel == null) yield break;

        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();

        float start = cg.alpha;
        float t = 0f;
        while (t < deactivateFadeDuration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, 0f, deactivateFadeDuration <= 0f ? 1f : (t / deactivateFadeDuration));
            yield return null;
        }
        cg.alpha = 0f;
        panel.SetActive(false);
    }
}
