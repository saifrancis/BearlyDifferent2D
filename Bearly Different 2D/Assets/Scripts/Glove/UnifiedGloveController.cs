// UnifiedGloveController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
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
    private Transform playerTransform;
    private Transform[] lanes;

    private const string PlayerTag = "Player";
    private const string PlayerName = "Player";
    private const string LaneTag = "Lane";
    private const string LanesParentName = "";

    private string preferredPortName = "COM3";
    private int preferredBaud = 9600;
    private bool expectsCsv = false;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private SerialPort _port;
#endif

    private Vector3 neutralTpl = new Vector3(1.05f, 1.90f, 10.21f);
    private Vector3 downTpl = new Vector3(10.37f, 0.24f, 1.92f);
    private Vector3 leftTpl = new Vector3(1.22f, 10.36f, 3.53f);
    private Vector3 rightTpl = new Vector3(-1.53f, -9.02f, 0.16f);
    private Vector3 upTpl = new Vector3(-9.53f, 2.12f, 1.73f);

    private float tolerance = 4f;
    private float alpha = 0.35f;
    private float dwellTime = 0.12f;
    private float neutralRearmDwell = 0.25f;

    private float gestureWindowSeconds = 0.6f;
    private float choiceCooldownSeconds = 0.25f;
    private int bendConfirmCount = 2;

    private int[] straightBaseline = new int[5] { 242, 258, 229, 274, 266 };
    private int bentDelta = 180;
    private int straightDelta = 40;

    private float fistWindowSeconds = 1.5f;
    private float fistCooldownSeconds = 0.75f;
    private float miniGame4FistWindowSeconds = 1.25f;
    private float miniGame4FistCooldownSeconds = 0.5f;

    private float laneCooldownSeconds = 0.15f;
    private float catchCooldownSeconds = 0.25f;
    private float catchRadius = 0.8f;
    private bool requireLeafInCatchZone = false;

    private bool logPoses = true;
    private bool logRawSerial = true;
    private int rawLogMax = 200;

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

    private static readonly Regex TripleNumberRegex =
        new Regex(@"([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)",
                  RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // sequence coroutine handle
    private Coroutine _seqRoutine;

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
    }

    private void Update()
    {
        while (_main.TryDequeue(out var act)) act();

        if (_windowActive && (Time.time - _windowStart) > gestureWindowSeconds) ResetFingerWindow();

        if (_fistWindowActive)
        {
            float len = mode == Mode.MiniGame4 ? miniGame4FistWindowSeconds : fistWindowSeconds;
            if (Time.time - _fistWindowStart > len) ResetFistWindow();
        }

        if (_hasSample && UsesPoses(mode)) UpdatePosePipeline();
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
        if (s.Contains("1page") || s.Contains("2page") || s.Contains("3page") || s.Contains("4page") || s.Contains("5page") || s.Contains("6page")) return Mode.Pages;
        return Mode.Pages;
    }

    private void AutoWireSceneRefs()
    {
        gameManager = null;
        beehiveGame1 = null;
        squirrelGame = null;
        beehiveMiniGame3Object = null;
        sliderManager = null;
        playerTransform = null;
        lanes = null;
        panelManager = null;

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
                break;
            case Mode.Pages:
                panelManager = FindInActiveSceneByTypeName("PanelManager");
                break;
            default:
                break;
        }
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

    private static GameObject[] SafeFindGameObjectsWithTag(string tag)
    {
        try { return GameObject.FindGameObjectsWithTag(tag); }
        catch { return Array.Empty<GameObject>(); }
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

        // sequences 1 to 5 for poses
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
        var mi = tp.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (mi != null) { mi.Invoke(target, null); return; }

        if (target is MonoBehaviour mb)
        {
            try { mb.gameObject.SendMessage(method, SendMessageOptions.DontRequireReceiver); } catch { }
        }
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

    private void HandleFlexBitfield(int[] bits)
    {
        if (!_windowActive) StartFingerWindow();
        int len = Mathf.Min(5, bits.Length);
        for (int k = 0; k < len; k++)
        {
            if (bits[k] == 1)
            {
                _bendStreak[k] = Mathf.Min(_bendStreak[k] + 1, 1000);
                if (_bendStreak[k] >= bendConfirmCount) _bits[k] = true;
            }
            else _bendStreak[k] = 0;
        }
        CheckPatternsMaybeChooseOrLane();
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

        // sequences 12 to 14 for Page 3 choices
        if ((mode == Mode.Pages && IsOnPage3()))
        {
            if (n == 1) FlashSequence(12);
            else if (n == 2) FlashSequence(13);
            else if (n == 3) FlashSequence(14);
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
        bool all = true;
        for (int i = 0; i < 5 && i < bits.Length; i++) if (bits[i] == 0) { all = false; break; }
        if (all) { FireFistIfCooldown(); ResetFistWindow(); return; }

        int len = Mathf.Min(5, bits.Length);
        for (int i = 0; i < len; i++)
        {
            if (_prevFlex[i] == 0 && bits[i] == 1) HandleFingerNameForFist(FingerNames[i]);
        }
        Array.Copy(bits, _prevFlex, len);
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
        else if (mode == Mode.Pages) CallIfPresent(panelManager, "TogglePanel", true);

        // sequences 7 to 10 for fist per mode
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

    private void TriggerCatchFromShake()
    {
        float now = Time.time;
        if (now - _lastCatchTime < catchCooldownSeconds) return;
        _lastCatchTime = now;

        if (playerTransform == null) return;
        Vector3 center = new Vector3(playerTransform.position.x, playerTransform.position.y, 0f);
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, catchRadius);
        foreach (var h in hits)
        {
            var leaf = h.GetComponent<Leaf>();
            if (leaf != null)
            {
                if (requireLeafInCatchZone && !leaf.IsInCatchZone()) continue;
                FishManager.Instance.CaughtLeaf(leaf);
            }
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

                            if (mode == Mode.MiniGame1 || mode == Mode.MiniGame2_1 || mode == Mode.MiniGame2_2 || mode == Mode.MiniGame2_3 || mode == Mode.MiniGame4
                                || mode == Mode.Pages)
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

                            if (mode != Mode.Pages && mode != Mode.MiniGame3)
                                _main.Enqueue(() => HandleFlexBitfieldForFist(ToBitsFromAnalog(copy)));
                            else
                                _main.Enqueue(() => HandleFlexBitfieldForFist(ToBitsFromAnalog(copy)));
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

    // sequence helpers
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
}
