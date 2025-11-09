using UnityEngine;
using TMPro;
using System.Collections;

public class ScoreTextFeedback : MonoBehaviour
{
    [Header("Pop + Flash Settings")]
    public float popScale = 1.2f;                
    public float duration = 0.15f;               
    public Color flashColor = new Color(1f, 0.9f, 0.3f); 
    public bool playOnEnable = false;            

    private TextMeshProUGUI tmp;
    private Coroutine effectRoutine;
    private Vector3 startScale;
    private Color startColor;

    void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            startScale = tmp.rectTransform.localScale;
            startColor = tmp.color;
        }
    }

    void OnEnable()
    {
        if (playOnEnable) Play();
    }

    public void Play()
    {
        if (tmp == null) return;

        if (effectRoutine != null)
            StopCoroutine(effectRoutine);

        effectRoutine = StartCoroutine(PopAndFlash());
    }

    private IEnumerator PopAndFlash()
    {
        var rt = tmp.rectTransform;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);

            float s = Mathf.Lerp(1f, popScale, 1f - Mathf.Pow(1f - k, 2f));
            rt.localScale = startScale * s;

            // flash color quickly
            tmp.color = Color.Lerp(flashColor, startColor, k);

            yield return null;
        }

        rt.localScale = startScale;
        tmp.color = startColor;
    }
}
