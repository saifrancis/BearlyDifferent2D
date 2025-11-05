using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class SquirrelMiniGame : MonoBehaviour
{
    [Header("References")]
    public Rigidbody2D playerRb;
    public Transform branchPivot;
    public Rigidbody2D beehiveRb;
    public TextMeshProUGUI messageText;

    [Header("Jump Settings")]
    public float jumpForce = 5f;
    public LayerMask groundLayer;
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;

    [Header("Rhythm Settings")]
    public float minJumpInterval = 0.4f;
    public float maxJumpInterval = 1.2f;
    public float branchShakeAmount = 5f;
    public float shakeDuration = 0.2f;
    public float requiredPerfectTime = 6f;

    [Header("Beehive Sprites")]
    public SpriteRenderer hiveSprite;
    public Sprite hiveNormalSprite;
    public Sprite hiveFallSprite;

    public float successFlashDuration = 0.12f;

    [Header("Help (Toggle with H)")]
    [SerializeField] private GameObject helpPanel;         // Assign a full-screen Panel under your Canvas
    [SerializeField] private bool helpStartsVisible = true;

    // --------------- Private state ---------------
    private bool isGrounded;
    private float lastJumpTime;
    private float perfectRhythmTimer;
    private bool gameWon;
    private bool isShaking;
    private Quaternion branchOriginalRotation;

    private Coroutine successFlashRoutine;

    private bool HelpVisible => helpPanel != null && helpPanel.activeSelf;

    // --------------- Unity ---------------
    void Start()
    {
        // Setup beehive and UI
        beehiveRb.bodyType = RigidbodyType2D.Kinematic;
        perfectRhythmTimer = 0f;
        if (messageText != null) messageText.text = "Jump to shake the beehive!";
        branchOriginalRotation = branchPivot.localRotation;

        // Initialize help panel + pause state
        if (helpPanel != null) helpPanel.SetActive(helpStartsVisible);
        ApplyPauseState(HelpVisible);
    }

    void Update()
    {
        // --- Help toggle FIRST so it works even while paused ---
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }

        // If help is visible, keep the game paused and block gameplay input/logic.
        if (HelpVisible) return;

        if (gameWon) return;

        // Ground check & input
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Keyboard input: Space → same behaviour as glove fist
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryJump();
        }

        if (isShaking)
        {
            TriggerSuccessFlash();
        }
    }

    // --------------- Help control ---------------
    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;

        bool next = !helpPanel.activeSelf;
        helpPanel.SetActive(next);
        ApplyPauseState(next);
    }

    private void ApplyPauseState(bool paused)
    {
        // Pauses physics, animations, coroutines using WaitForSeconds, etc.
        Time.timeScale = paused ? 0f : 1f;

        // Optional: ensure the panel captures input when visible
        // var cg = helpPanel != null ? helpPanel.GetComponent<CanvasGroup>() : null;
        // if (cg) { cg.interactable = paused; cg.blocksRaycasts = paused; }
    }

    // --------------- External glove input ---------------
    /// <summary>
    /// Called by the glove script when a fist is detected.
    /// Mirrors pressing Space (only jumps if grounded).
    /// </summary>
    public void OnFist()
    {
        if (gameWon || HelpVisible) return;
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        TryJump();
    }

    // --------------- Gameplay ---------------
    private void TryJump()
    {
        if (!isGrounded) return;

        float interval = Time.time - lastJumpTime;
        lastJumpTime = Time.time;

        // Apply jump
        playerRb.velocity = new Vector2(playerRb.velocity.x, 0f);
        playerRb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        // Rhythm feedback
        if (interval < minJumpInterval)
        {
            if (messageText) messageText.text = "Pip is exhausted... too fast!";
            perfectRhythmTimer = 0f;
        }
        else if (interval > maxJumpInterval)
        {
            if (messageText) messageText.text = "A little more effort!";
            perfectRhythmTimer = 0f;
        }
        else
        {
            if (messageText) messageText.text = "Perfect rhythm!";
            if (!isShaking) StartCoroutine(ShakeBranch());

            perfectRhythmTimer += interval;

            if (perfectRhythmTimer >= requiredPerfectTime)
            {
                WinGame();
            }
        }
    }

    IEnumerator ShakeBranch()
    {
        isShaking = true;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime; // stops while paused
            float zRotation = Mathf.Sin(elapsed * 40f) * branchShakeAmount;
            branchPivot.localRotation = Quaternion.Euler(0, 0, zRotation);
            yield return null;
        }

        branchPivot.localRotation = branchOriginalRotation;
        isShaking = false;
    }

    void WinGame()
    {
        gameWon = true;
        if (messageText) messageText.text = "The beehive falls!";

        beehiveRb.transform.SetParent(null);
        beehiveRb.bodyType = RigidbodyType2D.Dynamic;
        if (hiveSprite) hiveSprite.sprite = hiveFallSprite;

        StartCoroutine(GoToNextScene());
    }

    IEnumerator GoToNextScene()
    {
        // Will be paused by Time.timeScale = 0 if help is shown; resume when help closes.
        yield return new WaitForSeconds(5f);
        SceneManager.LoadScene("4Page_Four");
    }

    private void TriggerSuccessFlash()
    {
        if (successFlashRoutine != null)
            StopCoroutine(successFlashRoutine);

        successFlashRoutine = StartCoroutine(SuccessFlashCo());
    }

    private IEnumerator SuccessFlashCo()
    {
        if (hiveSprite != null)
        {
            hiveSprite.sprite = hiveFallSprite;
            yield return new WaitForSeconds(successFlashDuration);
            hiveSprite.sprite = hiveNormalSprite;
        }
        else
        {
            yield return null;
        }
    }
}
