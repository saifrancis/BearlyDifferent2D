using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class BeehiveMiniGame1 : MonoBehaviour
{
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

    void Start()
    {
        beehiveRb = beehive.GetComponent<Rigidbody2D>();
        if (beehiveRb == null) Debug.LogError("Beehive needs Rigidbody2D!");

        beehiveRb.bodyType = RigidbodyType2D.Kinematic;
        startPosition = beehive.position;
        UpdateHitsText();
    }

    void Update()
    {
        if (!isFalling)
        {
            SwingBeehive();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (IsInTargetZone())
                {
                    hitCount++;
                    UpdateHitsText();

                    if (hitCount >= requiredHits)
                    {
                        StartFalling();
                    }
                }
            }
        }
    }

    void SwingBeehive()
    {
        float xOffset = Mathf.Sin(Time.time * swingSpeed) * swingAmplitude;
        beehive.position = new Vector3(startPosition.x + xOffset, startPosition.y, startPosition.z);
    }

    bool IsInTargetZone()
    {
        float left = targetZone.position.x - targetZone.localScale.x / 2;
        float right = targetZone.position.x + targetZone.localScale.x / 2;

        float halfWidth = beehive.GetComponent<SpriteRenderer>().bounds.size.x / 2f;
        float hiveLeft = beehive.position.x - halfWidth;
        float hiveRight = beehive.position.x + halfWidth;

        float zoneLeft = targetZone.position.x - targetZone.localScale.x / 2f;
        float zoneRight = targetZone.position.x + targetZone.localScale.x / 2f;

        // Check if any part of the hive overlaps with the zone
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
        yield return new WaitForSeconds(delayAfterWin);

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogError("Next scene name is not set!");
        }
    }

    void UpdateHitsText()
    {
        if (hitsText != null)
            hitsText.text = $"Hits: {hitCount} / {requiredHits}";
    }
}
