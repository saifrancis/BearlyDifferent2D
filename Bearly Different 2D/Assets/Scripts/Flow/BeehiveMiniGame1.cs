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
    [SerializeField] private GameObject helpPanel;      
    [SerializeField] private bool helpStartsVisible = true;

    public ScoreTextFeedback scoreFeedback;

    public GameObject doneGO; 

    private bool HelpVisible => helpPanel != null && helpPanel.activeSelf;

    public WinText wt;

    public GameObject bo; 

    void Start()
    {
        beehiveRb = beehive.GetComponent<Rigidbody2D>();
        if (beehiveRb == null) Debug.LogError("Beehive needs Rigidbody2D!");

        beehiveRb.bodyType = RigidbodyType2D.Kinematic;
        startPosition = beehive.position;
        UpdateHitsText();

        if (helpPanel != null)
        {
            helpPanel.SetActive(helpStartsVisible);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }

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

    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;
        helpPanel.SetActive(!helpPanel.activeSelf);
    }

    void SwingBeehive()
    {
        float xOffset = Mathf.Sin(Time.time * swingSpeed) * swingAmplitude;
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

        bo.SetActive(false);
        doneGO.SetActive(true); 
        wt.PlayWin(); 

        StartCoroutine(LoadNextSceneAfterDelay());
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
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

        if (scoreFeedback != null)
            scoreFeedback.Play();
    }

    public void OnFist()
    {
        TryRegisterHit();
    }

    private void TryRegisterHit()
    {
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
