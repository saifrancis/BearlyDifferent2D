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
    public float requiredPerfectTime = 10f;

    private bool isGrounded;
    private float lastJumpTime;
    private float perfectRhythmTimer;
    private bool gameWon;
    private bool isShaking;
    private Quaternion branchOriginalRotation;

    public SpriteRenderer hiveSprite;    
    public Sprite hiveNormalSprite;       
    public Sprite hiveFallSprite;

    public float successFlashDuration = 0.12f;
    private Coroutine successFlashRoutine;

    void Start()
    {
        beehiveRb.bodyType = RigidbodyType2D.Kinematic;
        perfectRhythmTimer = 0f;
        messageText.text = "Jump to shake the beehive!";
        branchOriginalRotation = branchPivot.localRotation;
    }

    void Update()
    {
        if (gameWon) return;

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Keyboard input: Space → same behaviour as glove fist
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryJump();
        }

        if(isShaking)
        {
            TriggerSuccessFlash();
        }

    }

    /// <summary>
    /// Called by the glove script when a fist is detected.
    /// Mirrors pressing Space (only jumps if grounded).
    /// </summary>
    public void OnFist()
    {
        if (gameWon) return;
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        TryJump();
    }

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
            messageText.text = "Pip is exhausted... too fast!";
            perfectRhythmTimer = 0f;
        }
        else if (interval > maxJumpInterval)
        {
            messageText.text = "A little more effort!";
            perfectRhythmTimer = 0f;
        }
        else
        {
            messageText.text = "Perfect rhythm!";
            if (!isShaking) StartCoroutine(ShakeBranch());

            perfectRhythmTimer += interval;

            if (perfectRhythmTimer >= requiredPerfectTime)
            {
                WinGame();
            }
        }
    }

    System.Collections.IEnumerator ShakeBranch()
    {
        isShaking = true;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
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
        messageText.text = "The beehive falls!";

        beehiveRb.transform.SetParent(null);
        beehiveRb.bodyType = RigidbodyType2D.Dynamic;
        hiveSprite.sprite = hiveFallSprite;

        StartCoroutine(GoToNextScene());
    }

    System.Collections.IEnumerator GoToNextScene()
    {
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
        hiveSprite.sprite = hiveFallSprite;
        yield return new WaitForSeconds(successFlashDuration);
        hiveSprite.sprite = hiveNormalSprite;
    }


}
