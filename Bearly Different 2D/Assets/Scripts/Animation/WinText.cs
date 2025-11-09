using UnityEngine;
using TMPro;
using System.Collections;

public class WinText : MonoBehaviour
{
    [Header("UI References")]
    public CanvasGroup winGroup;
    public RectTransform winRect;

    [Header("Timing")]
    public float fadeInDuration = 0.4f;
    public float popScale = 1.2f;
    public float popDuration = 0.4f;
    public float wiggleAngle = 8f;
    public float wiggleSpeed = 6f;

    [Header("Optional")]
    public bool autoContinue = true;
    public float continueDelay = 2.5f;

    private Coroutine wiggleRoutine;

    [Header("Particles")]
    public ParticleSystem leavesFX;
    public bool emitLeaves = true;
    public int emitCountOverride = -1;

    void Awake()
    {
        // Make sure the particle runs even if Time.timeScale is zero
        if (leavesFX != null)
        {
            var main = leavesFX.main;
#if UNITY_2019_3_OR_NEWER
            main.useUnscaledTime = true;
#endif
            // Ensure object is active so Play actually runs
            leavesFX.gameObject.SetActive(true);

            // Optional safety: if no bursts are configured, do a small default burst
            var emission = leavesFX.emission;
            if (!emission.enabled) emission.enabled = true;
        }
    }

    public void PlayWin()
    {
        if (wiggleRoutine != null) StopCoroutine(wiggleRoutine);

        winRect.gameObject.SetActive(true);
        winGroup.alpha = 0f;
        winRect.localScale = Vector3.one * 0.6f;
        winRect.localRotation = Quaternion.identity;

        // Fire confetti before the sequence so the player sees it immediately
        if (emitLeaves && leavesFX != null)
        {
            // If you authored a Burst in the particle, Play is enough
            if (emitCountOverride < 0) leavesFX.Play();
            else leavesFX.Emit(Mathf.Max(0, emitCountOverride));
        }

        StartCoroutine(WinSequence());
    }

    IEnumerator WinSequence()
    {
        float t = 0f;
        while (t < popDuration)
        {
            t += Time.unscaledDeltaTime; // unscaled so it still animates if game is paused
            float k = Mathf.Clamp01(t / popDuration);

            winGroup.alpha = Mathf.Lerp(0f, 1f, k);
            float scale = Mathf.Lerp(0.6f, popScale, k);
            winRect.localScale = Vector3.one * scale;

            yield return null;
        }

        winRect.localScale = Vector3.one;
        wiggleRoutine = StartCoroutine(WiggleLoop());

        if (autoContinue)
        {
            yield return new WaitForSecondsRealtime(continueDelay);
            Debug.Log("Continue Trigger Here");
        }
    }

    IEnumerator WiggleLoop()
    {
        while (true)
        {
            float angle = Mathf.Sin(Time.unscaledTime * wiggleSpeed) * wiggleAngle;
            winRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
    }
}
