using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
using System.Linq;
#endif

public class MiniGame3Glove : MonoBehaviour
{
    [Header("Scene Refs")]
    public Transform playerTransform;       // assign your player object here
    public Transform[] zones;               // 3 lane anchors, index 0..2

    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 9600;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    public bool useDtr = true;
    public bool useRts = true;
#endif

    [Header("Lane Gestures (finger bit patterns)")]
    [Tooltip("Window length (s) to collect finger events (names or FLEX/FLEXA) before testing patterns")]
    public float gestureWindowSeconds = 1.0f;
    [Tooltip("Cooldown (s) between lane switches")]
    public float laneCooldownSeconds = 0.15f;

    [Header("Finger Bit Debounce (for FLEX 0/1)")]
    [Tooltip("Consecutive FLEX=1 lines required to mark a finger bent")]
    public int bendConfirmCount = 2;  // 2–3 recommended

    [Header("Hardcoded Baselines & Hysteresis (for FLEXA analog)")]
    [Tooltip("Straight baselines per finger (Thumb, Index, Middle, Ring, Pinkie)")]
    public int[] straightBaseline = new int[5] { 242, 258, 229, 274, 266 }; // from your log
    [Tooltip("Above this delta from baseline, we call it Bent")]
    public int bentDelta = 180;    // tune 140–220 if needed
    [Tooltip("Allows a small cushion to flip back to straight without jitter")]
    public int straightDelta = 40; // ~30–60 works well

    [Header("Catch via SHAKE (from Arduino)")]
    [Tooltip("Cooldown between two catch triggers after a shake (seconds)")]
    public float catchCooldownSeconds = 0.25f;
    [Tooltip("Catch radius around the player's position")]
    public float catchRadius = 0.8f;
    [Tooltip("Only score if the leaf is inside a CatchZone trigger")]
    public bool requireLeafInCatchZone = false;

    [Header("Debug")]
    public bool logRaw = true;
    public int rawLogMax = 200;

    // ---------- runtime state ----------
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private SerialPort _port;
#endif
    private Thread _thread;
    private volatile bool _running;
    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    // lanes
    private int _currentZone = 0;
    private float _lastLaneTime = -999f;

    // finger window (logical “bent” flags collected within gesture window)
    private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinkie" };
    private bool[] _bits = new bool[5];
    private float _windowStart = 0f;
    private bool _windowActive = false;

    // FLEX 0/1 debouncing
    private int[] _bendStreak = new int[5]; // T,I,M,R,P

    // FLEXA analog -> latched bent/straight with hysteresis
    private bool[] _latchedBent = new bool[5];

    // catch cooldown
    private float _lastCatchTime = -999f;

    private int _rawCount = 0;

    void Start()
    {
        if (playerTransform == null) Debug.LogWarning("[Glove] playerTransform not set.");
        if (zones == null || zones.Length < 3) Debug.LogWarning("[Glove] zones must have 3 entries.");

        // start in zone 0 (left)
        SnapToZone(0);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        TryOpenPort();
#else
        Debug.LogWarning("Serial runs on Windows Editor/Standalone only.");
#endif
    }

    void OnDestroy()
    {
        _running = false;
        try { _thread?.Join(200); } catch { }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
        try { _port?.Dispose(); } catch { }
        _port = null;
#endif
    }

    void Update()
    {
        // Drain serial-thread work
        while (_main.TryDequeue(out var act))
            act?.Invoke();

        // Expire finger window
        if (_windowActive && (Time.time - _windowStart) > gestureWindowSeconds)
            ResetFingerWindow();
    }

    // ===== Lane selection by finger patterns =====

    // Patterns (bool[5]: T, I, M, R, P)
    // Zone 1: T=1, I=0, M=1, R=1, P=1
    private static readonly bool[] PatternZ1 = { true, false, true, true, true };
    // Zone 2: T=1, I=0, M=0, R=1, P=1
    private static readonly bool[] PatternZ2 = { true, false, false, true, true };
    // Zone 3: T=1, I=1, M=0, R=0, P=0
    private static readonly bool[] PatternZ3 = { true, true, false, false, false };

    private void StartFingerWindow()
    {
        _windowActive = true;
        _windowStart = Time.time;
        for (int i = 0; i < 5; i++)
        {
            _bits[i] = false;
            _bendStreak[i] = 0; // also reset debounce counters
        }
        // keep _latchedBent as persistent state across windows (good for analog stability)
    }

    private void ResetFingerWindow()
    {
        _windowActive = false;
        _windowStart = 0f;
        for (int i = 0; i < 5; i++)
            _bits[i] = false;
    }

    private void AddFinger(string nameLower)
    {
        int idx = Array.IndexOf(Fingers, nameLower);
        if (idx < 0) return;

        if (!_windowActive) StartFingerWindow();
        _bits[idx] = true;

        CheckPatternsMaybeSwitchLane();
    }

    // FLEX 0/1 debounced
    private void HandleFlexBitfield(int[] bits) // 5 ints 0/1 order T,I,M,R,P
    {
        if (!_windowActive) StartFingerWindow();

        for (int i = 0; i < 5 && i < bits.Length; i++)
        {
            if (bits[i] == 1)
            {
                _bendStreak[i] = Mathf.Min(_bendStreak[i] + 1, 1000);
                if (_bendStreak[i] >= bendConfirmCount)
                    _bits[i] = true;
            }
            else
            {
                _bendStreak[i] = 0;
            }
        }

        CheckPatternsMaybeSwitchLane();
    }

    // FLEXA analog with hardcoded baselines + hysteresis
    private void HandleFlexAnalog(int[] vals) // vals length >=5, order T,I,M,R,P
    {
        if (!_windowActive) StartFingerWindow();

        for (int i = 0; i < 5 && i < vals.Length; i++)
        {
            int v = vals[i];
            int baseStraight = straightBaseline[i];
            int bentMin = baseStraight + bentDelta;
            int straightMax = baseStraight + straightDelta;

            if (_latchedBent[i])
            {
                // stay bent until we clearly go back toward straight
                if (v <= straightMax) _latchedBent[i] = false;
            }
            else
            {
                // become bent once clearly above bentMin
                if (v >= bentMin) _latchedBent[i] = true;
            }

            if (_latchedBent[i]) _bits[i] = true;
        }

        CheckPatternsMaybeSwitchLane();
    }

    private void CheckPatternsMaybeSwitchLane()
    {
        float now = Time.time;
        if (now - _lastLaneTime < laneCooldownSeconds) return;

        if (MatchesPattern(_bits, PatternZ1)) { _lastLaneTime = now; SnapToZone(0); ResetFingerWindow(); return; }
        if (MatchesPattern(_bits, PatternZ2)) { _lastLaneTime = now; SnapToZone(1); ResetFingerWindow(); return; }
        if (MatchesPattern(_bits, PatternZ3)) { _lastLaneTime = now; SnapToZone(2); ResetFingerWindow(); return; }
        // otherwise keep collecting until window expires
    }

    private static bool MatchesPattern(bool[] have, bool[] pat)
    {
        for (int i = 0; i < 5; i++)
        {
            if (have[i] != pat[i]) return false;
        }
        return true;
    }

    private void SnapToZone(int zoneIndex)
    {
        if (zones == null || zones.Length < 3 || playerTransform == null) return;
        zoneIndex = Mathf.Clamp(zoneIndex, 0, zones.Length - 1);
        _currentZone = zoneIndex;

        Vector3 pos = playerTransform.position;
        pos.x = zones[_currentZone].position.x;
        playerTransform.position = pos;
    }

    // ===== Catch via SHAKE (centered at player's position) =====

    private void TriggerCatchFromShake()
    {
        float now = Time.time;
        if (now - _lastCatchTime < catchCooldownSeconds) return;
        _lastCatchTime = now;

        DoCatchAtPlayer();
        // Debug.Log("[Glove] CATCH via SHAKE");
    }

    private void DoCatchAtPlayer()
    {
        if (playerTransform == null) return;

        // center catch at player's current position (syncs with visuals)
        Vector3 center = new Vector3(playerTransform.position.x, playerTransform.position.y, 0f);
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, catchRadius);

        int caught = 0;
        foreach (var h in hits)
        {
            var leaf = h.GetComponent<Leaf>();
            if (leaf != null)
            {
                if (requireLeafInCatchZone && !leaf.IsInCatchZone())
                    continue;

                FishManager.Instance.CaughtLeaf(leaf);
                caught++;
            }
        }
        // Debug.Log($"[Glove] Catch @x={center.x:0.00} r={catchRadius} hits={caught}");
    }

    // ===== Serial =====
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private string FixPortForDotNet(string p)
    {
        if (p.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(p.Substring(3), out var n) && n >= 10)
            return @"\\.\\" + p; // COM10+ fix
        return p;
    }

    private string AutoPickPort()
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            if (ports == null || ports.Length == 0) return null;
            if (ports.Contains(portName)) return portName;
            return ports
                .OrderBy(n => n.StartsWith("COM") && int.TryParse(n.Substring(3), out var x) ? x : -1)
                .Last();
        }
        catch { return null; }
    }

    private void TryOpenPort()
    {
        try
        {
            string chosen = AutoPickPort() ?? portName;
            string openName = FixPortForDotNet(chosen);

            _port = new SerialPort(openName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 50,
                WriteTimeout = 200,
                NewLine = "\n",
                DtrEnable = useDtr,
                RtsEnable = useRts
            };

            _port.Open();
            Thread.Sleep(800);

            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true };
            _thread.Start();

            Debug.Log($"[Glove] Opened {chosen} @ {baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open port. " + e.Message);
        }
    }

    private void ReadLoop()
    {
        var inv = CultureInfo.InvariantCulture;

        while (_running)
        {
            try
            {
                string line = _port.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string tline = line.Trim('\r', '\n', ' ');
                string low = tline.ToLowerInvariant();

                // Optional raw log (limited)
                if (logRaw && _rawCount < rawLogMax)
                {
                    bool isFinger = (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie");
                    bool isFlex = low.StartsWith("flex");
                    bool isFlexa = low.StartsWith("flexa");
                    bool isShake = (low == "shake");
                    if (isFinger || isFlex || isFlexa || isShake)
                    {
                        _main.Enqueue(() => Debug.Log($"[RAW] {tline}"));
                        _rawCount++;
                    }
                }

                // Finger names -> mark bit
                if (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie")
                {
                    string captured = low;
                    _main.Enqueue(() => AddFinger(captured));
                    continue;
                }

                // FLEX 0/1 bitfield: "FLEX 1 0 1 1 1"
                if (low.StartsWith("flex ") || low == "flex")
                {
                    string rest = low.StartsWith("flex ") ? low.Substring(5) : "";
                    var tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        int[] bits = new int[5];
                        bool ok = true;
                        for (int i = 0; i < 5; i++)
                            ok &= int.TryParse(tokens[i], NumberStyles.Integer, inv, out bits[i]);
                        if (ok)
                        {
                            int[] copy = (int[])bits.Clone();
                            _main.Enqueue(() => HandleFlexBitfield(copy));
                        }
                    }
                    continue;
                }

                // FLEXA analog: "FLEXA 378 812 745 790 820"
                if (low.StartsWith("flexa"))
                {
                    string rest = low.Substring(5).Trim();
                    var tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        int[] vals = new int[5];
                        bool ok = true;
                        for (int i = 0; i < 5; i++)
                            ok &= int.TryParse(tokens[i], NumberStyles.Integer, inv, out vals[i]);
                        if (ok)
                        {
                            int[] copy = (int[])vals.Clone();
                            _main.Enqueue(() => HandleFlexAnalog(copy));
                        }
                    }
                    continue;
                }

                // SHAKE line from Arduino
                if (low == "shake")
                {
                    _main.Enqueue(() => TriggerCatchFromShake());
                    continue;
                }

                // ignore other accel text; SHAKE is all we need now
            }
            catch (TimeoutException) { }
            catch (Exception)
            {
                Thread.Sleep(100);
            }
        }
    }
#endif
}
