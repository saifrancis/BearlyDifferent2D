using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class BeehiveMiniGame1 : MonoBehaviour
{
    [Header("Core")]
    public Transform beehive;
    public Transform targetZone;
    public float swingAmplitude = 0.5f;
    public float swingSpeed = 1f;
    public int requiredHits = 8;

    public float fallGravityScale = 1f;
    public TextMeshProUGUI hitsText;

    public string nextSceneName = "4Page_Four";
    public float delayAfterWin = 8f;

    private Rigidbody2D beehiveRb;
    private int hitCount = 0;
    private bool isFalling = false;
    private Vector3 startPosition;

    [Header("Sprites / Feedback")]
    public SpriteRenderer boSprite;
    public Sprite normalSprite;
    public Sprite hitFlashSprite;
    public float flashDuration = 0.12f;
    private Coroutine flashRoutine;

    public SpriteRenderer hiveSprite;
    public Sprite hiveNormalSprite;
    public Sprite hiveHitSprite;
    public float successFlashDuration = 0.12f;
    private Coroutine hiveFlashRoutine;

    [Header("Help (Toggle with H)")]
    [SerializeField] private GameObject helpPanel;       // Assign a full-screen UI Panel under your Canvas
    [SerializeField] private bool helpStartsVisible = true;

    private bool HelpVisible => helpPanel != null && helpPanel.activeSelf;

    void Start()
    {
        // Setup beehive
        beehiveRb = beehive.GetComponent<Rigidbody2D>();
        if (beehiveRb == null) Debug.LogError("Beehive needs Rigidbody2D!");

        beehiveRb.bodyType = RigidbodyType2D.Kinematic;
        startPosition = beehive.position;
        UpdateHitsText();

        // Initialize help panel & pause state
        if (helpPanel != null)
        {
            helpPanel.SetActive(helpStartsVisible);
        }
        ApplyPauseState(HelpVisible);
    }

    void Update()
    {
        // --- Handle Help toggle FIRST so it works even while paused ---
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }

        // While help is up, game MUST be paused and gameplay ignored.
        if (HelpVisible) return;

        // --- Normal gameplay ---
        if (!isFalling)
        {
            SwingBeehive();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                TryRegisterHit();
            }
        }

        if (isFalling)
        {
            if (hiveSprite != null && hiveHitSprite != null)
                hiveSprite.sprite = hiveHitSprite;
        }
    }

    // ===== Help control =====
    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;

        bool next = !helpPanel.activeSelf;
        helpPanel.SetActive(next);
        ApplyPauseState(next);
    }

    private void ApplyPauseState(bool paused)
    {
        // Pauses physics, animations, and coroutines using WaitForSeconds
        Time.timeScale = paused ? 0f : 1f;

        // Optional: ensure the panel captures input when visible
        // var cg = helpPanel != null ? helpPanel.GetComponent<CanvasGroup>() : null;
        // if (cg) { cg.interactable = paused; cg.blocksRaycasts = paused; }
    }

    // ===== Game logic =====
    void SwingBeehive()
    {
        float xOffset = Mathf.Sin(Time.unscaledTime * swingSpeed) * swingAmplitude;
        // NOTE: using UnscaledTime here makes the swing keep updating while paused.
        // If you want it frozen while help is open, use Time.time instead:
        // float xOffset = Mathf.Sin(Time.time * swingSpeed) * swingAmplitude;

        // If you DO want swing to stop while paused, switch back to Time.time above.
        beehive.position = new Vector3(startPosition.x + xOffset, startPosition.y, startPosition.z);
    }

    bool IsInTargetZone()
    {
        float halfWidth = beehive.GetComponent<SpriteRenderer>().bounds.size.x / 2f;
        float hiveLeft = beehive.position.x - halfWidth;
        float hiveRight = beehive.position.x + halfWidth;

        float zoneLeft = targetZone.position.x - targetZone.localScale.x / 2f;
        float zoneRight = targetZone.position.x + targetZone.localScale.x / 2f;

        return hiveRight >= zoneLeft && hiveLeft <= zoneRight;
    }

    void StartFalling()
    {
        isFalling = true;
        beehiveRb.bodyType = RigidbodyType2D.Dynamic;
        beehiveRb.gravityScale = fallGravityScale;

        StartCoroutine(LoadNextSceneAfterDelay());
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
        // This will be paused if Time.timeScale == 0.
        yield return new WaitForSeconds(delayAfterWin);

        if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
        else
            Debug.LogError("Next scene name is not set!");
    }

    void UpdateHitsText()
    {
        if (hitsText != null)
            hitsText.text = $"Hits: {hitCount} / {requiredHits}";
    }

    // Called by glove “fist” action
    public void OnFist()
    {
        if (HelpVisible) return; // block while help is open
        TryRegisterHit();
    }

    private void TryRegisterHit()
    {
        if (HelpVisible) return; // block while help is open
        if (isFalling) return;

        if (IsInTargetZone())
        {
            hitCount++;
            UpdateHitsText();

            if (hitCount >= requiredHits)
            {
                StartFalling();
            }
        }

        if (IsInTargetZone())
        {
            TriggerHitFlash();
            TriggerSuccessFlash();
        }
        else
        {
            TriggerHitFlash();
        }
    }

    private void TriggerHitFlash()
    {
        if (boSprite == null || hitFlashSprite == null || normalSprite == null) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(HitFlashCo());
    }

    private IEnumerator HitFlashCo()
    {
        boSprite.sprite = hitFlashSprite;
        yield return new WaitForSeconds(flashDuration);
        boSprite.sprite = normalSprite;
    }

    private void TriggerSuccessFlash()
    {
        if (hiveSprite == null || hiveHitSprite == null || hiveNormalSprite == null) return;

        if (hiveFlashRoutine != null)
            StopCoroutine(hiveFlashRoutine);

        hiveFlashRoutine = StartCoroutine(SuccessFlashCo());
    }

    private IEnumerator SuccessFlashCo()
    {
        hiveSprite.sprite = hiveHitSprite;
        yield return new WaitForSeconds(successFlashDuration);
        hiveSprite.sprite = hiveNormalSprite;
    }
}
