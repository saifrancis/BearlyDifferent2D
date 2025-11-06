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

    [Header("Particles (optional)")]
    public ParticleSystem leavesFX;       // <— drag your LeafConfetti here
    public bool emitLeaves = true;
    public int emitCountOverride = -1;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.W))
        {
            PlayWin();
        }
    }

    public void PlayWin()
    {
       
        if (wiggleRoutine != null) StopCoroutine(wiggleRoutine);

        winRect.gameObject.SetActive(true);
        winGroup.alpha = 0f;
        winRect.localScale = Vector3.one * 0.6f;
        winRect.localRotation = Quaternion.identity;

        if (emitLeaves && leavesFX != null)
        {
            // If you set a Burst in the system, just Play():
            if (emitCountOverride < 0) leavesFX.Play();
            else leavesFX.Emit(Mathf.Max(0, emitCountOverride)); // manual count
        }

        StartCoroutine(WinSequence());
    }

    IEnumerator WinSequence()
    {
        // ✅ Fade + Pop at same time
        float t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / popDuration);

            // fade
            winGroup.alpha = Mathf.Lerp(0f, 1f, k);

            // scale
            float scale = Mathf.Lerp(0.6f, popScale, k);
            winRect.localScale = Vector3.one * scale;

            yield return null;
        }

        // settle scale
        winRect.localScale = Vector3.one;

        // ✅ Start wiggle loop
        wiggleRoutine = StartCoroutine(WiggleLoop());

        // Optional wait before continuing
        if (autoContinue)
        {
            yield return new WaitForSeconds(continueDelay);
            Debug.Log("Continue Trigger Here");
            // You can change scene here if desired:
            // SceneManager.LoadScene("NextSceneHere");
        }
    }

    IEnumerator WiggleLoop()
    {
        while (true)
        {
            float angle = Mathf.Sin(Time.time * wiggleSpeed) * wiggleAngle;
            winRect.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
    }
}
