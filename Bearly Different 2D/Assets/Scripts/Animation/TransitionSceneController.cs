using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TransitionSceneController : MonoBehaviour
{
    [Header("UI")]
    public Image curlImage;                       // Assign the full-screen Image

    [Header("Frames (pick one direction or both)")]
    public Sprite[] framesRight;                  // Right-edge peel sequence (frame 0 -> last)
    public Sprite[] framesLeft;                   // Left-edge peel sequence

    [Header("Playback")]
    [Range(1, 60)] public int fps = 24;
    public bool useUnscaledTime = true;

    [Header("Audio (optional)")]
    public AudioSource sfx;                       // Optional rustle sound
    public int playSfxAtFrame = 2;                // When to play SFX

    void Start()
    {
        if (curlImage == null) curlImage = GetComponentInChildren<Image>(true);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // Read intent from loader
        var nextScene = TransitionLoader.NextSceneName;
        bool fromRight = TransitionLoader.FromRight;
        int requestedFps = TransitionLoader.RequestedFps > 0 ? TransitionLoader.RequestedFps : fps;

        if (string.IsNullOrEmpty(nextScene))
        {
            Debug.LogError("[Transition] No NextSceneName set. Returning to first scene in build.");
            nextScene = SceneManager.GetSceneByBuildIndex(0).name;
        }

        // Pick frames based on direction
        Sprite[] seq = fromRight ? framesRight : framesLeft;
        if (seq == null || seq.Length == 0)
        {
            // If opposite direction exists, fall back to it reversed
            var alt = fromRight ? framesLeft : framesRight;
            if (alt != null && alt.Length > 0)
            {
                seq = new Sprite[alt.Length];
                for (int i = 0; i < alt.Length; i++) seq[i] = alt[alt.Length - 1 - i];
            }
        }
        if (seq == null || seq.Length == 0)
        {
            Debug.LogError("[Transition] No frames assigned.");
            yield break;
        }

        float frameDur = 1f / requestedFps;

        // Start loading next scene in background but hold activation until halfway
        AsyncOperation op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Single);
        op.allowSceneActivation = false;

        int half = seq.Length / 2;

        for (int i = 0; i < seq.Length; i++)
        {
            curlImage.sprite = seq[i];

            if (sfx && i == Mathf.Clamp(playSfxAtFrame, 0, seq.Length - 1))
                sfx.Play();

            // At halfway point, allow the new scene to activate (swap underneath)
            if (i == half)
                op.allowSceneActivation = true;

            // Wait per-frame
            if (useUnscaledTime)
            {
                float t = 0f;
                while (t < frameDur) { t += Time.unscaledDeltaTime; yield return null; }
            }
            else
            {
                yield return new WaitForSeconds(frameDur);
            }
        }

        // Optional: tiny wait to ensure the next scene is active
        yield return null;
    }
}

