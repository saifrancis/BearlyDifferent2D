using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TransitionSceneController : MonoBehaviour
{
    [Header("UI")]
    public Image curlImage;

    [Header("Frames")]
    public Sprite[] framesRight;
    public Sprite[] framesLeft;

    [Header("Playback")]
    [Range(1, 60)] public int fps = 24;
    public bool useUnscaledTime = true;

    [Header("Audio")]
    public AudioClip rustleClip;            
    [Range(0f, 1f)] public float rustleVolume = 1f;
    public int playSfxAtFrame = 2;

    public string debugFallbackNextScene = "";

    AudioSource _sfx;

    void Awake()
    {
        EnsureAudioListener();
        EnsureAudioSource();
    }

    void EnsureAudioListener()
    {
        if (FindAnyObjectByType<AudioListener>() == null)
        {
            var go = new GameObject("TempAudioListener");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<AudioListener>();
            DontDestroyOnLoad(go);
        }
    }

    void EnsureAudioSource()
    {
        _sfx = GetComponent<AudioSource>();
        if (_sfx == null) _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.loop = false;
        _sfx.spatialBlend = 0f;  
        _sfx.volume = rustleVolume;
    }

    void Start()
    {
        if (curlImage == null) curlImage = GetComponentInChildren<Image>(true);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        var nextScene = !string.IsNullOrEmpty(TransitionLoader.NextSceneName)
            ? TransitionLoader.NextSceneName
            : debugFallbackNextScene;

        bool fromRight = TransitionLoader.FromRight;
        int requestedFps = TransitionLoader.RequestedFps > 0 ? TransitionLoader.RequestedFps : fps;

        if (string.IsNullOrEmpty(nextScene))
            yield break;

        // pick sequence
        Sprite[] seq = fromRight ? framesRight : framesLeft;
        if (seq == null || seq.Length == 0)
        {
            var alt = fromRight ? framesLeft : framesRight;
            if (alt != null && alt.Length > 0)
            {
                seq = new Sprite[alt.Length];
                for (int i = 0; i < alt.Length; i++) seq[i] = alt[alt.Length - 1 - i];
            }
        }
        if (seq == null || seq.Length == 0) yield break;

        float frameDur = 1f / requestedFps;

        var op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Single);
        op.allowSceneActivation = false;

        int half = seq.Length / 2;

        for (int i = 0; i < seq.Length; i++)
        {
            curlImage.sprite = seq[i];

            if (rustleClip && i == Mathf.Clamp(playSfxAtFrame, 0, seq.Length - 1))
                _sfx.PlayOneShot(rustleClip, rustleVolume); 

            if (i == half)
                op.allowSceneActivation = true;

            if (useUnscaledTime)
            {
                float t = 0f;
                while (t < frameDur) { t += Time.unscaledDeltaTime; yield return null; }
            }
            else yield return new WaitForSeconds(frameDur);
        }
    }
}
