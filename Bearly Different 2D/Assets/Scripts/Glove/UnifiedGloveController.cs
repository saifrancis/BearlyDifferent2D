// UnifiedGloveController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
#endif

public class UnifiedGloveController : MonoBehaviour
{
    public static UnifiedGloveController Instance;

    public enum Mode
    {
        Pages,
        MiniGame1,
        MiniGame2_1,
        MiniGame2_2,
        MiniGame2_3,
        MiniGame3,
        MiniGame4
    }

    public event Action<string> OnPose;
    public event Action<int> OnChoice;

    private Mode mode = Mode.Pages;

    private UnityEngine.Object gameManager;
    private UnityEngine.Object beehiveGame1;
    private UnityEngine.Object squirrelGame;
    private GameObject beehiveMiniGame3Object;
    private UnityEngine.Object sliderManager;
    private UnityEngine.Object panelManager;
    private UnityEngine.Object fishManager;

    private Transform playerTransform;
    private PlayerController cachedPlayerController;

    private Transform[] lanes;

    private const string PlayerTag = "Player";
    private const string PlayerName = "Player";
    private const string LaneTag = "Lane";
    private const string LanesParentName = "";

    [Header("Serial")]
    [SerializeField] private string preferredPortName = "COM3";
    [SerializeField] private int preferredBaud = 9600;
    [SerializeField] private bool expectsCsv = false;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private SerialPort _port;
#endif

    [Header("Pose templates")]
    [SerializeField] private Vector3 neutralTpl = new Vector3(1.05f, 1.90f, 10.21f);
    [SerializeField] private Vector3 downTpl = new Vector3(10.37f, 0.24f, 1.92f);
    [SerializeField] private Vector3 leftTpl = new Vector3(1.22f, 10.36f, 3.53f);
    [SerializeField] private Vector3 rightTpl = new Vector3(-1.53f, -9.02f, 0.16f);
    [SerializeField] private Vector3 upTpl = new Vector3(-9.53f, 2.12f, 1.73f);

    [Header("Pose settings")]
    [SerializeField] private float tolerance = 4f;
    [SerializeField] private float alpha = 0.35f;
    [SerializeField] private float dwellTime = 0.12f;
    [SerializeField] private float neutralRearmDwell = 0.25f;

    [Header("Finger window and patterns")]
    [SerializeField] private float gestureWindowSeconds = 0.6f;
    [SerializeField] private float choiceCooldownSeconds = 0.25f;
    [SerializeField] private int bendConfirmCount = 2;

    [SerializeField] private int[] straightBaseline = new int[5] { 242, 258, 229, 274, 266 };
    [SerializeField] private int bentDelta = 180;
    [SerializeField] private int straightDelta = 40;

    [Header("Fist and shake")]
    [SerializeField] private float fistWindowSeconds = 1.5f;
    [SerializeField] private float fistCooldownSeconds = 0.75f;
    [SerializeField] private float miniGame4FistWindowSeconds = 1.25f;
    [SerializeField] private float miniGame4FistCooldownSeconds = 0.5f;

    [Header("MiniGame3 catch")]
    [SerializeField] private float laneCooldownSeconds = 0.15f;
    [SerializeField] private float catchCooldownSeconds = 0.25f;
    [SerializeField] private float catchRadius = 0.8f;
    [SerializeField] private bool requireLeafInCatchZone = false;

    [Header("Debug")]
    [SerializeField] private bool logPoses = true;
    [SerializeField] private bool logRawSerial = true;
    [SerializeField] private int rawLogMax = 200;

    private Vector3 _filt;
    private string _lastEmittedPose;
    private string _candidatePose;
    private float _candidateStart;
    private bool _requireNeutralGate;
    private float _neutralSeenStart;

    private bool _windowActive;
    private float _windowStart;
    private readonly bool[] _bits = new bool[5];
    private readonly int[] _bendStreak = new int[5];
    private readonly bool[] _latchedBent = new bool[5];
    private float _lastChoiceTime = -999f;

    private bool _fistWindowActive;
    private float _fistWindowStart;
    private readonly HashSet<string> _fingersSeen = new HashSet<string>();
    private readonly int[] _prevFlex = new int[5];
    private float _lastFistTime = -999f;

    private int _currentLane;
    private float _lastLaneTime = -999f;
    private float _lastCatchTime = -999f;

    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    private Thread _thread;
    private volatile bool _running;
    private volatile bool _hasSample;
    private volatile float _ax, _ay, _az;
    private int _rawLogged;

    private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinkie" };

    private static readonly bool[] Pat1 = { true, false, true, true, true };
    private static readonly bool[] Pat2 = { true, false, false, true, true };
    private static readonly bool[] Pat3 = { true, true, false, false, false };

    private static readonly bool[] ThumbsUpPat = { false, true, true, true, true };
    [SerializeField] private float thumbsUpHoldSeconds = 0.5f;
    [SerializeField] private float thumbsUpCooldownSeconds = 0.75f;
    private float _thumbsUpStart = 0f;
    private bool _thumbsUpTracking = false;
    private float _lastThumbsUpTime = -999f;

    [SerializeField] private bool invertFlexBits = false;
    [SerializeField] private int[] fingerOrder = { 0, 1, 2, 3, 4 };

    private static readonly Regex TripleNumberRegex =
        new Regex(@"([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)",
                  RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private Coroutine _seqRoutine;

    private readonly bool[] _bentNow = new bool[5];
    private readonly float[] _bentExpiry = new float[5];
    [SerializeField] private float nameHoldSeconds = 2.0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        mode = GuessModeFromScene(SceneManager.GetActiveScene().name);
        ResetPerSceneRuntime();
    }

    private void Start()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        TryOpenPort(preferredPortName, preferredBaud);
#else
        Debug.LogWarning("Serial works on Windows editor and Windows standalone");
#endif
        AutoWireSceneRefs();
        if (mode == Mode.MiniGame3) SnapToLane(0);
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        _running = false;
        try { _thread?.Join(200); } catch { }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
        try { _port?.Dispose(); } catch { }
        _port = null;
#endif
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        mode = GuessModeFromScene(scene.name);
        ResetPerSceneRuntime();
        AutoWireSceneRefs();
        if (mode == Mode.MiniGame3) SnapToLane(0);
    }

    private static Mode GuessModeFromScene(string scene)
    {
        string s = scene.ToLowerInvariant();

        if (s.Contains("minigame_1")) return Mode.MiniGame1;
        if (s.Contains("minigame_2.1") || s.Contains("minigame_2_1")) return Mode.MiniGame2_1;
        if (s.Contains("minigame_2.2") || s.Contains("minigame_2_2")) return Mode.MiniGame2_2;
        if (s.Contains("minigame_2.3") || s.Contains("minigame_2_3")) return Mode.MiniGame2_3;
        if (s.Contains("minigame_3")) return Mode.MiniGame3;
        if (s.Contains("minigame_4")) return Mode.MiniGame4;

        if (s.Contains("0home") || s.Contains("home") || s.Contains("homepage") || s.Contains("menu") || s.Contains("start"))
            return Mode.Pages;

        if (s.Contains("1page") || s.Contains("2page") || s.Contains("3page") || s.Contains("4page") || s.Contains("5page") || s.Contains("6page"))
            return Mode.Pages;

        return Mode.Pages;
    }

    private void ResetPerSceneRuntime()
    {
        _requireNeutralGate = false;
        _neutralSeenStart = 0f;
        _candidatePose = null;
        _candidateStart = 0f;
        _lastEmittedPose = null;

        _windowActive = false;
        _windowStart = 0f;
        Array.Clear(_bits, 0, _bits.Length);
        Array.Clear(_bendStreak, 0, _bendStreak.Length);
        Array.Clear(_latchedBent, 0, _latchedBent.Length);

        _fistWindowActive = false;
        _fingersSeen.Clear();
        _fistWindowStart = 0f;
        Array.Clear(_prevFlex, 0, _prevFlex.Length);

        _lastLaneTime = -999f;
        _lastCatchTime = -999f;
        _lastChoiceTime = -999f;

        _thumbsUpStart = 0f;
        _thumbsUpTracking = false;
        _lastThumbsUpTime = -999f;

        Array.Clear(_bentNow, 0, _bentNow.Length);
        Array.Clear(_bentExpiry, 0, _bentExpiry.Length);
    }

    private void AutoWireSceneRefs()
    {
        gameManager = null;
        beehiveGame1 = null;
        squirrelGame = null;
        beehiveMiniGame3Object = null;
        sliderManager = null;
        panelManager = null;
        fishManager = null;
        playerTransform = null;
        cachedPlayerController = null;
        lanes = null;

        switch (mode)
        {
            case Mode.MiniGame1:
                gameManager = FindInActiveSceneByTypeName("GameManager");
                break;
            case Mode.MiniGame2_1:
                beehiveGame1 = FindInActiveSceneByTypeName("BeehiveMiniGame1");
                break;
            case Mode.MiniGame2_2:
                squirrelGame = FindInActiveSceneByTypeName("SquirrelMiniGame");
                break;
            case Mode.MiniGame2_3:
                beehiveMiniGame3Object = FindOwnerByTypeName("BeehiveMiniGame3");
                break;
            case Mode.MiniGame4:
                sliderManager = FindInActiveSceneByTypeName("SliderManager");
                break;
            case Mode.MiniGame3:
                ResolvePlayer();
                ResolveLanes();
                fishManager = FindInActiveSceneByTypeName("FishManager");
                cachedPlayerController = GameObject.FindObjectOfType<PlayerController>();
                if (cachedPlayerController != null) playerTransform = cachedPlayerController.transform;
                break;
            case Mode.Pages:
                panelManager = FindInActiveSceneByTypeName("PanelManager");
                break;
        }
    }

    private void Update()
    {
        while (_main.TryDequeue(out var act)) act();

        DecayNameBentStates();

        if (_thumbsUpTracking)
        {
            bool[] cur = new bool[5];
            for (int i = 0; i < 5; i++) cur[i] = _bentNow[i];
            UpdateThumbsUpDetection(cur);
        }

        if (_windowActive && (Time.time - _windowStart) > gestureWindowSeconds) ResetFingerWindow();

        if (_fistWindowActive)
        {
            float len = mode == Mode.MiniGame4 ? miniGame4FistWindowSeconds : fistWindowSeconds;
            if (Time.time - _fistWindowStart > len) ResetFistWindow();
        }

        if (_hasSample && UsesPoses(mode)) UpdatePosePipeline();
    }

    private void DecayNameBentStates()
    {
        float now = Time.time;
        for (int i = 0; i < 5; i++)
        {
            if (_bentNow[i] && now > _bentExpiry[i]) _bentNow[i] = false;
        }
    }

    private void NoteBentByNameIndex(int canonicalIndex)
    {
        if (canonicalIndex < 0 || canonicalIndex >= 5) return;
        _bentNow[canonicalIndex] = true;
        _bentExpiry[canonicalIndex] = Time.time + nameHoldSeconds;

        bool[] cur = new bool[5];
        for (int i = 0; i < 5; i++) cur[i] = _bentNow[i];
        UpdateThumbsUpDetection(cur);
    }

    private static GameObject[] SafeFindGameObjectsWithTag(string tag)
    {
        try { return GameObject.FindGameObjectsWithTag(tag); }
        catch { return Array.Empty<GameObject>(); }
    }

    private void ResolvePlayer()
    {
        var go = GameObject.FindWithTag(PlayerTag);
        if (go != null) { playerTransform = go.transform; return; }
        var byName = GameObject.Find(PlayerName);
        if (byName != null) { playerTransform = byName.transform; return; }

        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
            if (t.name.Equals(PlayerName, StringComparison.InvariantCultureIgnoreCase)) { playerTransform = t; return; }
        }
    }

    private void ResolveLanes()
    {
        var list = new List<Transform>();

        var tagged = SafeFindGameObjectsWithTag(LaneTag);
        if (tagged != null && tagged.Length > 0)
        {
            foreach (var g in tagged) list.Add(g.transform);
            lanes = list.OrderBy(t => t.position.x).ToArray();
            return;
        }

        if (!string.IsNullOrWhiteSpace(LanesParentName))
        {
            var parent = FindTransformByExactName(LanesParentName);
            if (parent != null)
            {
                foreach (Transform c in parent) list.Add(c);
                if (list.Count > 0) { lanes = list.OrderBy(t => t.position.x).ToArray(); return; }
            }
        }

        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("lane") || n.Contains("zone")) list.Add(t);
        }
        lanes = list.OrderBy(t => t.position.x).ToArray();
    }

    private static UnityEngine.Object FindInActiveSceneByTypeName(string typeName)
    {
        var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        foreach (var m in all)
        {
            if (!m) continue;
            var go = m.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (m.GetType().Name == typeName) return m;
        }
        return null;
    }

    private static GameObject FindOwnerByTypeName(string typeName)
    {
        var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        foreach (var m in all)
        {
            if (!m) continue;
            var go = m.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (m.GetType().Name == typeName) return go;
        }
        return null;
    }

    private static Transform FindTransformByExactName(string exact)
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in all)
        {
            if (!t || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
            if (t.name.Equals(exact, StringComparison.InvariantCultureIgnoreCase)) return t;
        }
        return null;
    }

    private static bool UsesPoses(Mode m) => m == Mode.Pages || m == Mode.MiniGame1 || m == Mode.MiniGame4;
    private static bool RequiresNeutralBetweenCommands(Mode m) => m == Mode.Pages || m == Mode.MiniGame1 || m == Mode.MiniGame4;

    private void UpdatePosePipeline()
    {
        Vector3 raw = new Vector3(_ax, _ay, _az);
        _filt = Vector3.Lerp(_filt, raw, alpha);

        string match = ClassifyPose(_filt);
        float now = Time.time;

        if (RequiresNeutralBetweenCommands(mode) && _requireNeutralGate)
        {
            if (match == "NEUTRAL")
            {
                if (_neutralSeenStart <= 0f) _neutralSeenStart = now;
                if (now - _neutralSeenStart >= neutralRearmDwell)
                {
                    _requireNeutralGate = false;
                    _neutralSeenStart = 0f;
                    _candidatePose = null;
                    _candidateStart = 0f;
                }
            }
            else _neutralSeenStart = 0f;
            return;
        }

        if (match == null) { _candidatePose = null; _candidateStart = 0f; return; }

        if (_candidatePose == null || _candidatePose != match)
        {
            _candidatePose = match;
            _candidateStart = now;
            return;
        }

        if (now - _candidateStart >= dwellTime && _lastEmittedPose != match)
        {
            _lastEmittedPose = match;
            if (logPoses) Debug.Log($"[POSE] {match}");
            OnPose?.Invoke(match);
            RoutePose(match);
            if (RequiresNeutralBetweenCommands(mode) && match != "NEUTRAL")
            {
                _requireNeutralGate = true;
                _neutralSeenStart = 0f;
            }
        }
    }

    private string ClassifyPose(Vector3 v)
    {
        bool isDown = Within(v, downTpl, tolerance);
        bool isNeutral = Within(v, neutralTpl, tolerance);
        bool isLeft = Within(v, leftTpl, tolerance);
        bool isRight = Within(v, rightTpl, tolerance);
        bool isUp = Within(v, upTpl, tolerance);

        if (!isDown && !isNeutral && !isLeft && !isRight && !isUp) return null;

        float bestErr = float.MaxValue;
        string best = null;

        TryBest(ref best, ref bestErr, "DOWN", SumAbsError(v, downTpl), isDown);
        TryBest(ref best, ref bestErr, "NEUTRAL", SumAbsError(v, neutralTpl), isNeutral);
        TryBest(ref best, ref bestErr, "LEFT", SumAbsError(v, leftTpl), isLeft);
        TryBest(ref best, ref bestErr, "RIGHT", SumAbsError(v, rightTpl), isRight);
        TryBest(ref best, ref bestErr, "UP", SumAbsError(v, upTpl), isUp);

        return best;
    }

    private static void TryBest(ref string label, ref float err, string candidate, float candErr, bool ok)
    { if (ok && candErr < err) { err = candErr; label = candidate; } }

    private static bool Within(Vector3 v, Vector3 t, float tol) =>
        Mathf.Abs(v.x - t.x) <= tol && Mathf.Abs(v.y - t.y) <= tol && Mathf.Abs(v.z - t.z) <= tol;

    private static float SumAbsError(Vector3 v, Vector3 t) =>
        Mathf.Abs(v.x - t.x) + Mathf.Abs(v.y - t.y) + Mathf.Abs(v.z - t.z);

    private void RoutePose(string pose)
    {
        if (mode == Mode.Pages)
        {
            if (pose == "RIGHT")
            {
                if (IsHomeScene())
                {
                    TryLoadGoToPageOneScene();
                }
                else
                {
                    if (!TryPanelRight())
                    {
                        TryLoadNextPageFromFlow();
                    }
                }
            }
            else if (pose == "LEFT")
            {
                if (!IsHomeScene())
                {
                    if (!TryPanelLeft())
                    {
                        TryLoadPrevPageFromFlow();
                    }
                }
            }
        }

        if (mode == Mode.MiniGame1)
        {
            CallIfPresent(gameManager, "GloveMoveLeft", pose == "LEFT");
            CallIfPresent(gameManager, "GloveMoveRight", pose == "RIGHT");
            CallIfPresent(gameManager, "GloveMoveUp", pose == "UP");
            CallIfPresent(gameManager, "GloveMoveDown", pose == "DOWN");
        }
        else if (mode == Mode.MiniGame4)
        {
            CallIfPresent(sliderManager, "GloveMoveLeft", pose == "LEFT");
            CallIfPresent(sliderManager, "GloveMoveRight", pose == "RIGHT");
            CallIfPresent(sliderManager, "GloveMoveUp", pose == "UP");
            CallIfPresent(sliderManager, "GloveMoveDown", pose == "DOWN");
        }

        if (pose == "RIGHT" && (mode == Mode.Pages || mode == Mode.MiniGame1 || mode == Mode.MiniGame4))
            FlashSequence(4);
        else if (pose == "LEFT" && (mode == Mode.Pages || mode == Mode.MiniGame1 || mode == Mode.MiniGame4))
            FlashSequence(4);
        else if (pose == "UP" && (mode == Mode.MiniGame1 || mode == Mode.MiniGame4))
            FlashSequence(4);
        else if (pose == "DOWN" && (mode == Mode.MiniGame1 || mode == Mode.MiniGame4))
            FlashSequence(4);
        else if (pose == "NEUTRAL" && (mode == Mode.Pages || mode == Mode.MiniGame1 || mode == Mode.MiniGame4))
            FlashSequence(5);
    }

    private static void CallIfPresent(UnityEngine.Object target, string method, bool condition)
    {
        if (!condition || target == null) return;

        var tp = target.GetType();
        var mi = tp.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi != null) { mi.Invoke(target, null); return; }

        if (target is MonoBehaviour mb)
        {
            try { mb.gameObject.SendMessage(method, SendMessageOptions.DontRequireReceiver); } catch { }
        }
    }

    private void TryInvokeMany(UnityEngine.Object target, params string[] methodNames)
    {
        if (target == null) return;
        var tp = target.GetType();

        foreach (var name in methodNames)
        {
            var mi = tp.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                try { mi.Invoke(target, null); Debug.Log($"[Help] Invoked {tp.Name}.{name}"); }
                catch { }
                return;
            }
        }

        if (target is MonoBehaviour mb)
        {
            foreach (var name in methodNames)
            {
                try { mb.gameObject.SendMessage(name, SendMessageOptions.DontRequireReceiver); Debug.Log($"[Help] SendMessage {mb.name}.{name}"); return; }
                catch { }
            }
        }
    }

    private IEnumerable<UnityEngine.Object> GetHelpTargets()
    {
        switch (mode)
        {
            case Mode.Pages:
                if (panelManager != null) yield return panelManager;
                break;
            case Mode.MiniGame1:
                if (gameManager != null) yield return gameManager;
                break;
            case Mode.MiniGame2_1:
                if (beehiveGame1 != null) yield return beehiveGame1;
                break;
            case Mode.MiniGame2_2:
                if (squirrelGame != null) yield return squirrelGame;
                break;
            case Mode.MiniGame2_3:
                if (beehiveMiniGame3Object != null) yield return beehiveMiniGame3Object;
                break;
            case Mode.MiniGame3:
                if (fishManager != null) yield return fishManager;
                break;
            case Mode.MiniGame4:
                if (sliderManager != null) yield return sliderManager;
                break;
        }
    }

    private void OpenHelpForCurrentScene()
    {
        string[] names = { "OpenHelpPanel", "OpenHelp", "ShowHelp", "ToggleHelpPanel", "ToggleHelp", "TogglePanel" };
        foreach (var target in GetHelpTargets()) TryInvokeMany(target, names);
    }

    private void StartFingerWindow()
    {
        _windowActive = true;
        _windowStart = Time.time;
        for (int k = 0; k < 5; k++) { _bits[k] = false; _bendStreak[k] = 0; }
    }

    private void ResetFingerWindow()
    {
        _windowActive = false;
        _windowStart = 0f;
        for (int k = 0; k < 5; k++) _bits[k] = false;
    }

    private void AddFingerBentEvidence(int fingerIndex)
    {
        if (!_windowActive) StartFingerWindow();
        if (fingerIndex < 0 || fingerIndex >= 5) return;
        _bits[fingerIndex] = true;
        CheckPatternsMaybeChooseOrLane();
    }

    private bool[] NormalizeBits(int[] bits)
    {
        bool[] cur = new bool[5];
        for (int i = 0; i < 5; i++)
        {
            int src = fingerOrder[i];
            int v = (src < bits.Length) ? bits[src] : 0;
            if (invertFlexBits) v = 1 - v;
            cur[i] = (v == 1);
        }
        return cur;
    }

    private bool[] NormalizeLatched()
    {
        bool[] cur = new bool[5];
        for (int i = 0; i < 5; i++)
        {
            int src = fingerOrder[i];
            bool bent = (src < _latchedBent.Length) ? _latchedBent[src] : false;
            cur[i] = bent;
        }
        return cur;
    }

    private string BitsToString(bool[] cur)
    {
        char[] c = new char[5];
        for (int i = 0; i < 5; i++) c[i] = cur[i] ? '1' : '0';
        return new string(c);
    }

    private int[] ToBitsFromAnalogThresholded(int[] vals)
    {
        int[] bits = new int[5];
        for (int k = 0; k < 5 && k < vals.Length; k++)
        {
            int baseStraight = straightBaseline[k];
            int bentMin = baseStraight + bentDelta;
            bits[k] = vals[k] >= bentMin ? 1 : 0;
        }
        return bits;
    }

    private void HandleFlexBitfield(int[] bits)
    {
        if (!_windowActive) StartFingerWindow();

        int len = Mathf.Min(5, bits.Length);
        for (int k = 0; k < len; k++)
        {
            int b = invertFlexBits ? 1 - bits[k] : bits[k];
            if (b == 1)
            {
                _bendStreak[k] = Mathf.Min(_bendStreak[k] + 1, 1000);
                if (_bendStreak[k] >= bendConfirmCount) _bits[k] = true;
            }
            else _bendStreak[k] = 0;
        }
        CheckPatternsMaybeChooseOrLane();

        bool[] cur = NormalizeBits(bits);
        Debug.Log($"[FLEX bits norm] {BitsToString(cur)}");
        UpdateThumbsUpDetection(cur);

        for (int i = 0; i < 5; i++) { _bentNow[i] = cur[i]; _bentExpiry[i] = Time.time + nameHoldSeconds; }
    }

    private void HandleFlexAnalog(int[] vals)
    {
        if (!_windowActive) StartFingerWindow();

        int len = Mathf.Min(5, vals.Length);
        for (int k = 0; k < len; k++)
        {
            int baseStraight = straightBaseline[k];
            int bentMin = baseStraight + bentDelta;
            int straightMax = baseStraight + straightDelta;

            if (_latchedBent[k]) { if (vals[k] <= straightMax) _latchedBent[k] = false; }
            else { if (vals[k] >= bentMin) _latchedBent[k] = true; }

            if (_latchedBent[k]) _bits[k] = true;
        }
        CheckPatternsMaybeChooseOrLane();

        bool[] cur = NormalizeLatched();
        Debug.Log($"[FLEXA latched norm] {BitsToString(cur)}");
        UpdateThumbsUpDetection(cur);

        for (int i = 0; i < 5; i++) { _bentNow[i] = cur[i]; _bentExpiry[i] = Time.time + nameHoldSeconds; }
    }

    private void CheckPatternsMaybeChooseOrLane()
    {
        if (mode == Mode.Pages)
        {
            float now = Time.time;
            if (now - _lastChoiceTime < choiceCooldownSeconds) return;

            if (Matches(_bits, Pat1)) { _lastChoiceTime = now; EmitChoice(1); ResetFingerWindow(); return; }
            if (Matches(_bits, Pat2)) { _lastChoiceTime = now; EmitChoice(2); ResetFingerWindow(); return; }
            if (Matches(_bits, Pat3)) { _lastChoiceTime = now; EmitChoice(3); ResetFingerWindow(); return; }
            return;
        }

        if (mode == Mode.MiniGame3)
        {
            float now = Time.time;
            if (now - _lastLaneTime < laneCooldownSeconds) return;

            if (Matches(_bits, Pat1)) { _lastLaneTime = now; SnapToLane(0); FlashSequence(12); ResetFingerWindow(); return; }
            if (Matches(_bits, Pat2)) { _lastLaneTime = now; SnapToLane(1); FlashSequence(13); ResetFingerWindow(); return; }
            if (Matches(_bits, Pat3)) { _lastLaneTime = now; SnapToLane(2); FlashSequence(14); ResetFingerWindow(); return; }
        }
    }

    private static bool Matches(bool[] have, bool[] pat)
    {
        for (int k = 0; k < 5; k++) if (have[k] != pat[k]) return false;
        return true;
    }

    private void EmitChoice(int n)
    {
        if (logPoses) Debug.Log($"[CHOICE] {n}");
        OnChoice?.Invoke(n);

        if (mode == Mode.Pages && IsOnPage3())
        {
            if (n == 1) FlashSequence(12);
            else if (n == 2) FlashSequence(13);
            else if (n == 3) FlashSequence(14);
        }
    }

    private void UpdateThumbsUpDetection(bool[] currentBent)
    {
        bool isThumbsUpNow = true;
        for (int i = 0; i < 5; i++)
        {
            bool want = ThumbsUpPat[i];
            bool have = i < currentBent.Length ? currentBent[i] : false;
            if (have != want) { isThumbsUpNow = false; break; }
        }

        float now = Time.time;

        if (!isThumbsUpNow)
        {
            _thumbsUpTracking = false;
            _thumbsUpStart = 0f;
            return;
        }

        if (!_thumbsUpTracking)
        {
            _thumbsUpTracking = true;
            _thumbsUpStart = now;
            Debug.Log("[THUMBS UP] hold started");
            return;
        }

        if (now - _thumbsUpStart >= thumbsUpHoldSeconds)
        {
            if (now - _lastThumbsUpTime >= thumbsUpCooldownSeconds)
            {
                _lastThumbsUpTime = now;
                Debug.Log("[THUMBS UP] triggered");
                OpenHelpForCurrentScene();
                FlashSequence(5);
            }
            _thumbsUpTracking = false;
            _thumbsUpStart = 0f;
        }
    }

    private void HandleFingerNameForFist(string nameLower)
    {
        if (!_fistWindowActive)
        {
            _fistWindowActive = true;
            _fistWindowStart = Time.time;
            _fingersSeen.Clear();
            if (logRawSerial) Debug.Log($"[FIST] start by {nameLower.ToUpperInvariant()}");
        }
        _fingersSeen.Add(nameLower);
        TryCompleteFistIfReady();
    }

    private void HandleFlexBitfieldForFist(int[] bits)
    {
        bool allBent = true;
        for (int i = 0; i < 5 && i < bits.Length; i++)
        {
            int v = invertFlexBits ? 1 - bits[i] : bits[i];
            if (v == 0) { allBent = false; break; }
        }
        if (allBent) { FireFistIfCooldown(); ResetFistWindow(); return; }

        int len = Mathf.Min(5, bits.Length);
        for (int i = 0; i < len; i++)
        {
            int cur = invertFlexBits ? 1 - bits[i] : bits[i];
            if ((_prevFlex[i] == 0) && (cur == 1)) HandleFingerNameForFist(FingerNames[i]);
            _prevFlex[i] = cur;
        }
    }

    private void TryCompleteFistIfReady()
    {
        if (_fingersSeen.Count == 5) { FireFistIfCooldown(); ResetFistWindow(); }
    }

    private void FireFistIfCooldown()
    {
        float now = Time.time;
        float cd = mode == Mode.MiniGame4 ? miniGame4FistCooldownSeconds : fistCooldownSeconds;
        if (now - _lastFistTime < cd) return;
        _lastFistTime = now;

        Debug.Log("SELECT (FIST)");

        if (mode == Mode.MiniGame1) CallIfPresent(gameManager, "GloveSelectOrSwapArm", true);
        else if (mode == Mode.MiniGame2_1) CallIfPresent(beehiveGame1, "OnFist", true);
        else if (mode == Mode.MiniGame2_2) CallIfPresent(squirrelGame, "OnFist", true);
        else if (mode == Mode.MiniGame2_3 && beehiveMiniGame3Object != null) beehiveMiniGame3Object.SendMessage("SpawnRock", SendMessageOptions.DontRequireReceiver);
        else if (mode == Mode.MiniGame4) CallIfPresent(sliderManager, "GloveSelect", true);

        if (mode == Mode.MiniGame1 || mode == Mode.MiniGame4) FlashSequence(7);
        else if (mode == Mode.MiniGame2_1) FlashSequence(8);
        else if (mode == Mode.MiniGame2_2) FlashSequence(9);
        else if (mode == Mode.MiniGame2_3) FlashSequence(10);
    }

    private void ResetFistWindow()
    {
        _fistWindowActive = false;
        _fingersSeen.Clear();
        _fistWindowStart = 0f;
    }

    private void SnapToLane(int index)
    {
        if (lanes == null || lanes.Length == 0 || playerTransform == null) return;
        index = Mathf.Clamp(index, 0, lanes.Length - 1);
        _currentLane = index;
        Vector3 p = playerTransform.position;
        p.x = lanes[_currentLane].position.x;
        playerTransform.position = p;
    }

    // Leaf catch on shake plus bite flash trigger
    private void TriggerCatchFromShake()
    {
        float now = Time.time;
        if (now - _lastCatchTime < catchCooldownSeconds) return;
        _lastCatchTime = now;

        if (playerTransform == null)
        {
            if (cachedPlayerController == null)
                cachedPlayerController = GameObject.FindObjectOfType<PlayerController>();
            if (cachedPlayerController != null)
                playerTransform = cachedPlayerController.transform;
        }

        bool caughtAny = false;

        if (playerTransform != null)
        {
            Vector3 center = new Vector3(playerTransform.position.x, playerTransform.position.y, 0f);
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, catchRadius);

            foreach (var h in hits)
            {
                var leaf = h.GetComponent<Leaf>();
                if (leaf == null) continue;
                if (requireLeafInCatchZone && !leaf.IsInCatchZone()) continue;

                FishManager.Instance.CaughtLeaf(leaf);
                caughtAny = true;
            }
        }

        TryTriggerBiteFlash();

        if (caughtAny) Debug.Log("[SHAKE] caught leaf near player");
    }

    private void TryTriggerBiteFlash()
    {
        if (cachedPlayerController == null)
            cachedPlayerController = GameObject.FindObjectOfType<PlayerController>();
        if (cachedPlayerController == null) return;

        var mi = typeof(PlayerController).GetMethod("TriggerBiteFlash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi != null)
        {
            try { mi.Invoke(cachedPlayerController, null); } catch { }
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static string FixPortForDotNet(string p)
    {
        if (p.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(p.Substring(3), out int num) && num >= 10) return @"\\.\\" + p;
        }
        return p;
    }

    private string AutoPickPort(string preferred)
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            if (ports == null || ports.Length == 0) return null;
            if (!string.IsNullOrWhiteSpace(preferred) && ports.Contains(preferred)) return preferred;
            return ports.OrderBy(n => { if (n.StartsWith("COM") && int.TryParse(n.Substring(3), out var x)) return x; return -1; }).Last();
        }
        catch { return null; }
    }

    private void TryOpenPort(string preferred, int baud)
    {
        try
        {
            string chosen = AutoPickPort(preferred) ?? preferred ?? "COM3";
            string openName = FixPortForDotNet(chosen);

            _port = new SerialPort(openName, baud, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 50,
                WriteTimeout = 200,
                NewLine = "\n",
                DtrEnable = true,
                RtsEnable = true
            };

            _port.Open();
            Thread.Sleep(800);

            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true };
            _thread.Start();

            Debug.Log($"[UnifiedGlove] Opened {chosen} at {baud}");
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open port. " + e.Message);
        }
    }

    private void ReadLoop()
    {
        while (_running)
        {
            try
            {
                string line = _port.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string t = line.Trim();
                string low = t.ToLowerInvariant();

                if (logRawSerial && _rawLogged < rawLogMax)
                {
                    bool isFinger = low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie";
                    bool isFlex = low.StartsWith("flex");
                    bool isFlexA = low.StartsWith("flexa");
                    bool isShake = low == "shake";
                    if (isFinger || isFlex || isFlexA || isShake)
                    {
                        _main.Enqueue(() => Debug.Log($"[RAW] {t}"));
                        _rawLogged++;
                    }
                }

                if (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie")
                {
                    int idx = Array.IndexOf(FingerNames, low);

                    if (mode == Mode.Pages || mode == Mode.MiniGame3)
                        _main.Enqueue(() => AddFingerBentEvidence(idx));

                    if (mode == Mode.MiniGame1 || mode == Mode.MiniGame2_1 || mode == Mode.MiniGame2_2 || mode == Mode.MiniGame2_3 || mode == Mode.MiniGame4
                        || mode == Mode.Pages)
                        _main.Enqueue(() => HandleFingerNameForFist(low));

                    _main.Enqueue(() => NoteBentByNameIndex(idx));
                }
                else if (low.StartsWith("flex ") || low == "flex")
                {
                    string rest = low.StartsWith("flex ") ? low.Substring(5) : "";
                    string[] tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        var inv = CultureInfo.InvariantCulture;
                        int[] bits = new int[5];
                        bool ok = true;
                        for (int k = 0; k < 5; k++) ok &= int.TryParse(tokens[k], NumberStyles.Integer, inv, out bits[k]);

                        if (ok)
                        {
                            int[] copy = (int[])bits.Clone();

                            if (mode == Mode.Pages || mode == Mode.MiniGame3)
                                _main.Enqueue(() => HandleFlexBitfield(copy));

                            _main.Enqueue(() => HandleFlexBitfieldForFist((int[])copy.Clone()));
                        }
                    }
                }
                else if (low.StartsWith("flexa"))
                {
                    string rest = low.Length > 5 ? low.Substring(5).Trim() : "";
                    string[] tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        var inv = CultureInfo.InvariantCulture;
                        int[] vals = new int[5];
                        bool ok = true;
                        for (int k = 0; k < 5; k++) ok &= int.TryParse(tokens[k], NumberStyles.Integer, inv, out vals[k]);

                        if (ok)
                        {
                            int[] copy = (int[])vals.Clone();

                            if (mode == Mode.Pages || mode == Mode.MiniGame3)
                                _main.Enqueue(() => HandleFlexAnalog(copy));

                            _main.Enqueue(() => HandleFlexBitfieldForFist(ToBitsFromAnalogThresholded(copy)));
                        }
                    }
                }
                else if (low == "shake")
                {
                    if (mode == Mode.MiniGame3)
                    {
                        _main.Enqueue(() =>
                        {
                            TriggerCatchFromShake();
                            FlashSequence(11);
                        });
                    }
                }
                else
                {
                    if (UsesPoses(mode))
                    {
                        var inv = CultureInfo.InvariantCulture;

                        if (expectsCsv)
                        {
                            string[] p = t.Split(',');
                            if (p.Length >= 3
                                && float.TryParse(p[0], NumberStyles.Float, inv, out float ax1)
                                && float.TryParse(p[1], NumberStyles.Float, inv, out float ay1)
                                && float.TryParse(p[2], NumberStyles.Float, inv, out float az1))
                            {
                                float axv = ax1, ayv = ay1, azv = az1;
                                _main.Enqueue(() => { _ax = axv; _ay = ayv; _az = azv; _hasSample = true; });
                            }
                        }
                        else
                        {
                            if (TryParseTripleRegex(t, out float ax2, out float ay2, out float az2))
                            {
                                float axv = ax2, ayv = ay2, azv = az2;
                                _main.Enqueue(() => { _ax = axv; _ay = ayv; _az = azv; _hasSample = true; });
                            }
                        }
                    }
                }
            }
            catch (TimeoutException) { }
            catch (Exception)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static bool TryParseTripleRegex(string line, out float ax, out float ay, out float az)
    {
        ax = ay = az = 0f;
        var m = TripleNumberRegex.Match(line);
        if (!m.Success || m.Groups.Count < 4) return false;

        var inv = CultureInfo.InvariantCulture;
        string sx = m.Groups[1].Value.Replace(',', '.');
        string sy = m.Groups[2].Value.Replace(',', '.');
        string sz = m.Groups[3].Value.Replace(',', '.');

        return float.TryParse(sx, NumberStyles.Float, inv, out ax)
            && float.TryParse(sy, NumberStyles.Float, inv, out ay)
            && float.TryParse(sz, NumberStyles.Float, inv, out az);
    }
#endif

    private static int[] ToBitsFromAnalog(int[] vals)
    {
        int[] bits = new int[5];
        for (int i = 0; i < 5 && i < vals.Length; i++) bits[i] = vals[i] > 0 ? 1 : 0;
        return bits;
    }

    private void SendSeq(int n)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try { if (_port != null && _port.IsOpen) _port.WriteLine($"S {n}"); } catch (Exception e) { Debug.LogWarning($"SEQ send fail {e.Message}"); }
#endif
    }

    public void FlashSequence(int n, float seconds = 2f)
    {
        if (_seqRoutine != null) { StopCoroutine(_seqRoutine); _seqRoutine = null; }
        SendSeq(n);
        _seqRoutine = StartCoroutine(StopSeqAfter(seconds));
    }

    private IEnumerator StopSeqAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        SendSeq(0);
        _seqRoutine = null;
    }

    private static bool IsOnPage3()
    {
        var s = SceneManager.GetActiveScene().name.ToLowerInvariant();
        return s.Contains("3page");
    }

    private bool IsHomeScene()
    {
        string s = SceneManager.GetActiveScene().name.ToLowerInvariant();
        return s.Contains("0home") || s.Contains("home") || s.Contains("homepage") || s.Contains("menu") || s.Contains("start");
    }

    private void TryLoadGoToPageOneScene()
    {
        var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        foreach (var m in all)
        {
            if (!m) continue;
            var go = m.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

            var t = m.GetType();
            if (t.Name != "GoToPageOne") continue;

            var f = t.GetField("targetScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            string next = null;

            if (f != null) next = f.GetValue(m) as string;
            if (string.IsNullOrWhiteSpace(next))
            {
                var p = t.GetProperty("targetScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string)) next = p.GetValue(m) as string;
            }

            if (!string.IsNullOrWhiteSpace(next))
            {
                SceneManager.LoadScene(next);
                return;
            }
        }

        Debug.LogWarning("GoToPageOne not found, cannot start");
    }

    private bool TryPanelRight()
    {
        if (panelManager == null) return false;
        TryInvokeMany(panelManager, "OnRight", "Right", "Next", "NextPanel", "GoNext", "Advance");
        return true;
    }

    private bool TryPanelLeft()
    {
        if (panelManager == null) return false;
        TryInvokeMany(panelManager, "OnLeft", "Left", "Prev", "Previous", "PrevPanel", "GoPrev", "Back");
        return true;
    }

    private void TryLoadNextPageFromFlow()
    {
        string next = FindSceneNameFromFlowField("nextScene");
        if (!string.IsNullOrEmpty(next)) SceneManager.LoadScene(next);
    }

    private void TryLoadPrevPageFromFlow()
    {
        string prev = FindSceneNameFromFlowField("previousScene", "prevScene", "backScene");
        if (!string.IsNullOrEmpty(prev)) SceneManager.LoadScene(prev);
    }

    private string FindSceneNameFromFlowField(params string[] fieldNames)
    {
        var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        foreach (var m in all)
        {
            if (!m) continue;
            var go = m.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

            string tn = m.GetType().Name;
            bool looksFlow =
                tn.IndexOf("PageFlow", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                tn.IndexOf("Flow", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                tn.IndexOf("Navigator", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                tn.IndexOf("GoToNextPage", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                tn.IndexOf("GoToPrevPage", StringComparison.InvariantCultureIgnoreCase) >= 0;

            if (!looksFlow) continue;

            foreach (var fname in fieldNames)
            {
                var f = m.GetType().GetField(fname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                var p = m.GetType().GetProperty(fname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string))
                {
                    var v = p.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
        }
        return null;
    }
}
