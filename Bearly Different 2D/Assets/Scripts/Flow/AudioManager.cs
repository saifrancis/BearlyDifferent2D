using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Scenes")]
    [SerializeField] private string homeSceneName = "0HomePage";

    [Header("Home Tracks (play together on Home)")]
    [SerializeField] private AudioClip homeTrackA;
    [SerializeField] private AudioClip homeTrackB;

    [Header("Game Tracks (play together on every other scene)")]
    [SerializeField] private AudioClip gameTrackA;
    [SerializeField] private AudioClip gameTrackB;

    [Header("Levels / Fades")]
    [Range(0f, 1f)] public float homeVolumeA = 1f;
    [Range(0f, 1f)] public float homeVolumeB = 1f;
    [Range(0f, 1f)] public float gameVolumeA = 1f;
    [Range(0f, 1f)] public float gameVolumeB = 1f;
    [SerializeField] private float crossfadeSeconds = 2f;

    // We keep two pairs so we can crossfade without stopping audio
    private AudioSource[] currentPair = new AudioSource[2];
    private AudioSource[] standbyPair = new AudioSource[2];

    private enum Mode { None, Home, Game }
    private Mode currentMode = Mode.None;
    private bool initialized;

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Build 4 audio sources (2 active + 2 standby for crossfades)
        currentPair[0] = CreateSource("Current_A");
        currentPair[1] = CreateSource("Current_B");
        standbyPair[0] = CreateSource("Standby_A");
        standbyPair[1] = CreateSource("Standby_B");

        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    void Start()
    {
        // Start appropriate pair for the initial scene (no fade on first start)
        var startScene = SceneManager.GetActiveScene().name;
        if (startScene == homeSceneName) PlayImmediateHome();
        else PlayImmediateGame();
        initialized = true;
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    private AudioSource CreateSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f; // 2D
        return src;
    }

    private void OnSceneChanged(Scene prev, Scene next)
    {
        // Decide which pair should be active based on scene name
        if (next.name == homeSceneName)
        {
            if (currentMode != Mode.Home)
                CrossfadeTo(homeTrackA, homeTrackB, homeVolumeA, homeVolumeB, Mode.Home);
        }
        else
        {
            if (currentMode != Mode.Game)
                CrossfadeTo(gameTrackA, gameTrackB, gameVolumeA, gameVolumeB, Mode.Game);
        }
    }

    // ---- Immediate start (first scene) ----
    private void PlayImmediateHome()
    {
        SetClipsAndPlay(currentPair, homeTrackA, homeTrackB, homeVolumeA, homeVolumeB);
        SetVolumes(standbyPair, 0f, 0f);
        currentMode = Mode.Home;
    }

    private void PlayImmediateGame()
    {
        SetClipsAndPlay(currentPair, gameTrackA, gameTrackB, gameVolumeA, gameVolumeB);
        SetVolumes(standbyPair, 0f, 0f);
        currentMode = Mode.Game;
    }

    // ---- Crossfade between pairs ----
    private void CrossfadeTo(AudioClip clipA, AudioClip clipB, float volA, float volB, Mode newMode)
    {
        // If first time and not initialized, start immediately (safety)
        if (!initialized)
        {
            SetClipsAndPlay(currentPair, clipA, clipB, volA, volB);
            currentMode = newMode;
            return;
        }

        // Prep standby with target clips at zero volume
        SetClipsAndPlay(standbyPair, clipA, clipB, 0f, 0f);
        StopAllCoroutines();
        StartCoroutine(CrossfadeRoutine(volA, volB, newMode));
    }

    private System.Collections.IEnumerator CrossfadeRoutine(float targetA, float targetB, Mode newMode)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, crossfadeSeconds);

        // Capture starting volumes
        float startCurA = currentPair[0].volume;
        float startCurB = currentPair[1].volume;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime; // unaffected by game pause
            float k = Mathf.Clamp01(t / dur);

            // Fade out current
            currentPair[0].volume = Mathf.Lerp(startCurA, 0f, k);
            currentPair[1].volume = Mathf.Lerp(startCurB, 0f, k);

            // Fade in standby
            standbyPair[0].volume = Mathf.Lerp(0f, targetA, k);
            standbyPair[1].volume = Mathf.Lerp(0f, targetB, k);

            yield return null;
        }

        // Swap pairs: standby becomes current; stop old sources
        SwapPairs();

        currentMode = newMode;
    }

    private void SwapPairs()
    {
        // Stop the old current (now faded out)
        currentPair[0].Stop();
        currentPair[1].Stop();

        // Swap arrays
        var tmp = currentPair;
        currentPair = standbyPair;
        standbyPair = tmp;
    }

    private void SetClipsAndPlay(AudioSource[] pair, AudioClip a, AudioClip b, float volA, float volB)
    {
        pair[0].clip = a;
        pair[1].clip = b;

        pair[0].volume = Mathf.Clamp01(volA);
        pair[1].volume = Mathf.Clamp01(volB);

        if (a != null && !pair[0].isPlaying) pair[0].Play();
        if (b != null && !pair[1].isPlaying) pair[1].Play();
    }

    private void SetVolumes(AudioSource[] pair, float volA, float volB)
    {
        pair[0].volume = Mathf.Clamp01(volA);
        pair[1].volume = Mathf.Clamp01(volB);
    }
}
