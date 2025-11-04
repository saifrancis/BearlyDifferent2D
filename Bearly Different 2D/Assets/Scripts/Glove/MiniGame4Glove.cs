using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
using System.Linq;
#endif

public class MiniGame4Glove : MonoBehaviour
{
    [Header("Bind to puzzle")]
    public SliderManager slider;   // <- drag your SliderManager in the inspector

    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 9600;
    [Tooltip("False because Arduino prints labeled accel lines, not CSV")]
    public bool expectsCsv = false;

    [Header("Pose Templates (m/s^2)")]
    public Vector3 neutralTpl = new Vector3(1.05f, 1.90f, 10.21f);
    public Vector3 downTpl = new Vector3(10.37f, 0.24f, 1.92f);
    public Vector3 leftTpl = new Vector3(1.22f, 10.36f, 3.53f);
    public Vector3 rightTpl = new Vector3(-1.53f, -9.02f, 0.16f);
    public Vector3 upTpl = new Vector3(-9.53f, 2.12f, 1.73f);

    [Header("Matching / Filtering")]
    public float tolerance = 3f;
    [Range(0f, 1f)] public float alpha = 0.35f;
    public float dwellTime = 0.12f;

    [Header("Flow")]
    [Tooltip("Require NEUTRAL to be seen between two motion commands")]
    public bool requireNeutralBetweenCommands = true;

    [Header("SELECT (fist)")]
    [Tooltip("Window length (seconds) that starts when the FIRST finger bends")]
    public float fistWindowSeconds = 1.25f;
    [Tooltip("Cooldown between two SELECTs")]
    public float selectRearmSeconds = 0.5f;

    [Header("Debug")]
    public bool debugFist = true;
    [Tooltip("Log raw finger/FLEX lines only (never accel)")]
    public bool logRawSerial = true;
    public int rawLogMax = 200;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [Header("Serial Lines (advanced)")]
    public bool useDtr = true;
    public bool useRts = true;
    private SerialPort _port;
#endif

    // -------- main-thread queue for serial events --------
    private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

    // -------- runtime (accel) --------
    private Thread _thread;
    private volatile bool _running;
    private volatile bool _hasSample;
    private volatile float _ax, _ay, _az;    // parsed accel (not logged)
    private Vector3 _filt;

    private string _candidatePose = null;
    private float _candidateStart = 0f;
    private string _lastEmittedPose = null;

    private bool _requireNeutral = false; // for motions
    private float _lastSelectTime = -999f;

    // -------- deterministic fist window --------
    private static readonly string[] AllFingers = { "thumb", "index", "middle", "ring", "pinkie" };
    private bool _fistWindowActive = false;
    private float _fistWindowStart = 0f;
    private readonly HashSet<string> _fingersSeenThisWindow = new HashSet<string>();

    // FLEX bitfield tracking to detect 0->1 transitions
    private int[] _prevFlex = new int[5] { 0, 0, 0, 0, 0 };

    private int _rawLogged = 0;

    private static readonly Regex TripleNumberRegex =
        new Regex(@"([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)",
                  RegexOptions.Compiled | RegexOptions.CultureInvariant);

    void Start()
    {
        if (slider == null) Debug.LogWarning("[MiniGame4Glove] SliderManager not set.");
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
        while (_mainThread.TryDequeue(out var act))
            act?.Invoke();

        if (!_hasSample) return;

        // filter accel (no logging)
        Vector3 raw = new Vector3(_ax, _ay, _az);
        _filt = Vector3.Lerp(_filt, raw, alpha);

        // instant neutral re-arm for motions
        if (requireNeutralBetweenCommands && _requireNeutral && Within(_filt, neutralTpl, tolerance))
        {
            _requireNeutral = false;
            _candidatePose = null;
            _candidateStart = 0f;
            Debug.Log("NEUTRAL");
            return;
        }

        // pose classification
        string pose = ClassifyByWindow(_filt);
        HandlePose(pose);

        // expire fist window if running
        if (_fistWindowActive)
        {
            float now = Time.time;
            if (now - _fistWindowStart > fistWindowSeconds)
            {
                if (debugFist)
                {
                    string missing = string.Join(", ",
                        Array.FindAll(AllFingers, f => !_fingersSeenThisWindow.Contains(f)));
                    Debug.Log($"[FIST] window expired ({fistWindowSeconds:0.00}s). Missing: {missing}");
                }
                ResetFistWindow();
            }
        }
    }

    // ---------- Pose helpers ----------
    private void HandlePose(string pose)
    {
        if (pose == null) { _candidatePose = null; _candidateStart = 0f; return; }

        float now = Time.time;

        if (_candidatePose == null || _candidatePose != pose)
        {
            _candidatePose = pose;
            _candidateStart = now;
            return;
        }

        if (now - _candidateStart < dwellTime) return;
        if (_lastEmittedPose == pose) return;

        _lastEmittedPose = pose;

        if (pose == "NEUTRAL") { Debug.Log("NEUTRAL"); return; }

        bool consumed = false;
        switch (pose)
        {
            case "LEFT": slider?.GloveMoveLeft(); consumed = true; Debug.Log("LEFT"); break;
            case "RIGHT": slider?.GloveMoveRight(); consumed = true; Debug.Log("RIGHT"); break;
            case "UP": slider?.GloveMoveUp(); consumed = true; Debug.Log("UP"); break;
            case "DOWN": slider?.GloveMoveDown(); consumed = true; Debug.Log("DOWN"); break;
        }

        if (consumed && requireNeutralBetweenCommands)
            _requireNeutral = true;
    }

    private string ClassifyByWindow(Vector3 v)
    {
        bool isDown = Within(v, downTpl, tolerance);
        bool isNeutral = Within(v, neutralTpl, tolerance);
        bool isLeft = Within(v, leftTpl, tolerance);
        bool isRight = Within(v, rightTpl, tolerance);
        bool isUp = Within(v, upTpl, tolerance);

        if (!isDown && !isNeutral && !isLeft && !isRight && !isUp) return null;

        float bestErr = float.MaxValue;
        string best = null;

        if (isDown) TryBest(ref best, ref bestErr, "DOWN", SumAbsError(v, downTpl));
        if (isNeutral) TryBest(ref best, ref bestErr, "NEUTRAL", SumAbsError(v, neutralTpl));
        if (isLeft) TryBest(ref best, ref bestErr, "LEFT", SumAbsError(v, leftTpl));
        if (isRight) TryBest(ref best, ref bestErr, "RIGHT", SumAbsError(v, rightTpl));
        if (isUp) TryBest(ref best, ref bestErr, "UP", SumAbsError(v, upTpl));
        return best;
    }

    private static bool Within(Vector3 v, Vector3 t, float tol) =>
        Mathf.Abs(v.x - t.x) <= tol && Mathf.Abs(v.y - t.y) <= tol && Mathf.Abs(v.z - t.z) <= tol;
    private static float SumAbsError(Vector3 v, Vector3 t) =>
        Mathf.Abs(v.x - t.x) + Mathf.Abs(v.y - t.y) + Mathf.Abs(v.z - t.z);
    private static void TryBest(ref string label, ref float err, string candidate, float candErr)
    { if (candErr < err) { err = candErr; label = candidate; } }

    // ---------- FINGER / FIST logic ----------
    private void HandleFingerName(string low)
    {
        if (!_fistWindowActive)
        {
            _fistWindowActive = true;
            _fistWindowStart = Time.time;
            _fingersSeenThisWindow.Clear();
            if (debugFist) Debug.Log($"[FIST] window started (len {fistWindowSeconds:0.00}s) by {low.ToUpperInvariant()}");
        }

        _fingersSeenThisWindow.Add(low);
        LogFistWindowStatus(low);
        TryCompleteFistIfReady();
    }

    private void HandleFlexBitfield(int[] flex) // order: thumb,index,middle,ring,pinkie
    {
        // detect 0->1 transitions
        for (int i = 0; i < 5; i++)
        {
            if (_prevFlex[i] == 0 && flex[i] == 1)
            {
                string fname = AllFingers[i];
                HandleFingerName(fname);
            }
        }
        Array.Copy(flex, _prevFlex, 5);
    }

    private void TryCompleteFistIfReady()
    {
        float now = Time.time;

        if (_fingersSeenThisWindow.Count == 5)
        {
            if (now - _lastSelectTime < selectRearmSeconds)
            {
                if (debugFist)
                {
                    float left = selectRearmSeconds - (now - _lastSelectTime);
                    Debug.Log($"[FIST] all five seen, but cooldown {left:0.00}s remaining");
                }
                return;
            }

            _lastSelectTime = now;
            Debug.Log("SELECT (FIST)");
            // Do NOT set _requireNeutral here – movement should be free right after select
            slider?.GloveSelect();
            ResetFistWindow();
        }
    }

    private void LogFistWindowStatus(string lastFinger)
    {
        if (!debugFist) return;

        string got = string.Join(",", _fingersSeenThisWindow);
        string missing = string.Join(",",
            Array.FindAll(AllFingers, f => !_fingersSeenThisWindow.Contains(f)));
        float remain = Mathf.Max(0f, fistWindowSeconds - (Time.time - _fistWindowStart));
        Debug.Log($"[FINGER] {lastFinger.ToUpperInvariant()} | In-window: [{got}] | Missing: [{missing}] | {remain:0.00}s left");
    }

    private void ResetFistWindow()
    {
        _fistWindowActive = false;
        _fingersSeenThisWindow.Clear();
        _fistWindowStart = 0f;
    }

    // -------------------- Serial I/O --------------------
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private string FixPortForDotNet(string p)
    {
        if (p.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(p.Substring(3), out var n) && n >= 10)
            return @"\\.\\" + p;
        return p;
    }

    private string AutoPickPort()
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            if (ports == null || ports.Length == 0) return null;
            if (ports.Contains(portName)) return portName;
            return ports.OrderBy(n =>
            {
                if (n.StartsWith("COM") && int.TryParse(n.Substring(3), out var x)) return x;
                return -1;
            }).Last();
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

            Debug.Log($"[MiniGame4Glove] Opened {chosen} @ {baudRate}");
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

                // RAW logging: only finger names or FLEX lines; never accel lines
                if (logRawSerial && _rawLogged < rawLogMax)
                {
                    bool isFinger = (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie");
                    bool isFlex = low.StartsWith("flex");
                    if (isFinger || isFlex)
                    {
                        _mainThread.Enqueue(() => Debug.Log($"[RAW] {tline}"));
                        _rawLogged++;
                    }
                }

                // 1) finger names
                if (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie")
                {
                    _mainThread.Enqueue(() => HandleFingerName(low));
                    continue;
                }

                // 2) FLEX bitfield: "FLEX 1 0 1 1 0"
                if (low.StartsWith("flex"))
                {
                    string rest = low.Substring(4).Trim();
                    var tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        int[] flex = new int[5];
                        bool ok = true;
                        for (int i = 0; i < 5; i++)
                            ok &= int.TryParse(tokens[i], NumberStyles.Integer, inv, out flex[i]);
                        if (ok)
                        {
                            int[] copy = (int[])flex.Clone();
                            _mainThread.Enqueue(() => HandleFlexBitfield(copy));
                        }
                    }
                    continue;
                }

                // 3) accelerometer numbers from labeled line (parsed but never logged)
                if (!expectsCsv)
                {
                    if (TryParseTriple(tline, out float ax, out float ay, out float az))
                    { _ax = ax; _ay = ay; _az = az; _hasSample = true; }
                }
                else
                {
                    var p = tline.Split(',');
                    if (p.Length >= 3
                        && float.TryParse(p[0], NumberStyles.Float, inv, out float ax)
                        && float.TryParse(p[1], NumberStyles.Float, inv, out float ay)
                        && float.TryParse(p[2], NumberStyles.Float, inv, out float az))
                    { _ax = ax; _ay = ay; _az = az; _hasSample = true; }
                }
            }
            catch (TimeoutException) { }
            catch (Exception)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static bool TryParseTriple(string line, out float ax, out float ay, out float az)
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
}
