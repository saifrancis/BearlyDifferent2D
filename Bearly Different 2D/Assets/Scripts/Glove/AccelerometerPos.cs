using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
#endif

public class AccelerometerPos : MonoBehaviour
{
    public event Action<string> OnPose;   // DOWN, NEUTRAL, LEFT, RIGHT, UP
    public event Action<int> OnChoice;    // 1, 2, 3

    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 9600;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    public bool useDtr = true;
    public bool useRts = true;
#endif
    [Tooltip("True for ax,ay,az lines. False for labeled lines like X: a Y: b Z: c")]
    public bool expectsCsv = false;

    [Header("Debug")]
    public bool logPoses = true;   // logs only pose labels and choice numbers

    [Header("Enable")]
    public bool readOrientationPoses = true;
    public bool readFingerChoices = true;

    [Header("Pose templates m per s squared")]
    private Vector3 neutralTpl = new Vector3(1.05f, 1.90f, 10.21f);
    private Vector3 downTpl = new Vector3(10.37f, 0.24f, 1.92f);
    private Vector3 leftTpl = new Vector3(1.22f, 10.36f, 3.53f);
    private Vector3 rightTpl = new Vector3(-1.53f, -9.02f, 0.16f);
    private Vector3 upTpl = new Vector3(-9.53f, 2.12f, 1.73f);

    [Header("Pose params")]
    public float tolerance = 1.6f;
    [Range(0f, 1f)] public float alpha = 0.35f;
    public float dwellTime = 0.12f;
    public float neutralRearmDwell = 0.25f;

    private Vector3 _filt;
    private string _lastEmitted;
    private string _candidate;
    private float _candidateStart;
    private bool _requireNeutral;
    private float _neutralSeenStart;

    [Header("Choice window and cooldown")]
    public float gestureWindowSeconds = 0.6f;
    public float choiceCooldownSeconds = 0.25f;

    [Header("FLEX 0 or 1 debouncing")]
    public int bendConfirmCount = 2;

    [Header("FLEXA baselines and hysteresis")]
    public int[] straightBaseline = new int[5] { 242, 258, 229, 274, 266 };
    public int bentDelta = 180;
    public int straightDelta = 40;

    // Choice patterns, true means bent, false means straight, order Thumb Index Middle Ring Pinkie
    private static readonly bool[] Pat1 = { true, false, true, true, true }; // only index straight
    private static readonly bool[] Pat2 = { true, false, false, true, true }; // index and middle straight
    private static readonly bool[] Pat3 = { true, true, false, false, false }; // middle ring pinkie straight

    private bool _windowActive;
    private float _windowStart;
    private readonly bool[] _bits = new bool[5];
    private readonly int[] _bendStreak = new int[5];
    private readonly bool[] _latchedBent = new bool[5];
    private float _lastChoiceTime = -999f;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private SerialPort _port;
#endif
    private Thread _thread;
    private volatile bool _running;
    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    private volatile float _ax, _ay, _az;
    private volatile bool _hasSample;

    private static readonly Regex TripleNumberRegex =
        new Regex(@"([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)[^\d+-]+([-+]?\d+(?:[.,]\d+)?)",
                  RegexOptions.Compiled);

    void Start()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        TryOpenPort();
#else
        Debug.LogWarning("Serial is supported on Windows Editor and Windows Standalone");
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
        while (_main.TryDequeue(out var act)) act();

        if (_windowActive && (Time.time - _windowStart) > gestureWindowSeconds)
            ResetFingerWindow();

        if (readOrientationPoses && _hasSample)
            UpdatePosePipeline();
    }

    // Poses
    private void UpdatePosePipeline()
    {
        Vector3 raw = new Vector3(_ax, _ay, _az);
        _filt = Vector3.Lerp(_filt, raw, alpha);

        string match = ClassifyByWindow(_filt);
        float now = Time.time;

        if (_requireNeutral)
        {
            if (match == "NEUTRAL")
            {
                if (_neutralSeenStart <= 0f) _neutralSeenStart = now;
                if (now - _neutralSeenStart >= neutralRearmDwell)
                {
                    _requireNeutral = false;
                    _neutralSeenStart = 0f;
                    _candidate = null;
                    _candidateStart = 0f;
                }
            }
            else
            {
                _neutralSeenStart = 0f;
            }
            return;
        }

        if (match == null)
        {
            _candidate = null;
            _candidateStart = 0f;
            return;
        }

        if (_candidate == null || _candidate != match)
        {
            _candidate = match;
            _candidateStart = now;
            return;
        }

        if (now - _candidateStart >= dwellTime && _lastEmitted != match)
        {
            _lastEmitted = match;
            if (logPoses) Debug.Log($"[POSE] {match}");
            OnPose?.Invoke(match);
            _requireNeutral = true;
            _neutralSeenStart = 0f;
        }
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

        if (isDown) TryBest(ref best, ref bestErr, "DOWN", v, downTpl);
        if (isNeutral) TryBest(ref best, ref bestErr, "NEUTRAL", v, neutralTpl);
        if (isLeft) TryBest(ref best, ref bestErr, "LEFT", v, leftTpl);
        if (isRight) TryBest(ref best, ref bestErr, "RIGHT", v, rightTpl);
        if (isUp) TryBest(ref best, ref bestErr, "UP", v, upTpl);

        return best;
    }

    private static bool Within(Vector3 v, Vector3 t, float tol)
    {
        return Mathf.Abs(v.x - t.x) <= tol
            && Mathf.Abs(v.y - t.y) <= tol
            && Mathf.Abs(v.z - t.z) <= tol;
    }

    private static float SumAbsError(Vector3 v, Vector3 t)
    {
        return Mathf.Abs(v.x - t.x) + Mathf.Abs(v.y - t.y) + Mathf.Abs(v.z - t.z);
    }

    private static void TryBest(ref string label, ref float err, string candidate, Vector3 v, Vector3 tpl)
    {
        float candErr = SumAbsError(v, tpl);
        if (candErr < err) { err = candErr; label = candidate; }
    }

    // Choices
    private void StartFingerWindow()
    {
        _windowActive = true;
        _windowStart = Time.time;
        for (int k = 0; k < 5; k++)
        {
            _bits[k] = false;
            _bendStreak[k] = 0;
        }
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
        CheckPatternsMaybeChoose();
    }

    private void HandleFlexBitfield(int[] bits)
    {
        if (!_windowActive) StartFingerWindow();

        int len = bits.Length < 5 ? bits.Length : 5;
        for (int k = 0; k < len; k++)
        {
            if (bits[k] == 1)
            {
                int s = _bendStreak[k] + 1;
                _bendStreak[k] = s > 1000 ? 1000 : s;
                if (_bendStreak[k] >= bendConfirmCount) _bits[k] = true;
            }
            else
            {
                _bendStreak[k] = 0;
            }
        }
        CheckPatternsMaybeChoose();
    }

    private void HandleFlexAnalog(int[] vals)
    {
        if (!_windowActive) StartFingerWindow();

        int len = vals.Length < 5 ? vals.Length : 5;
        for (int k = 0; k < len; k++)
        {
            int baseStraight = straightBaseline[k];
            int bentMin = baseStraight + bentDelta;
            int straightMax = baseStraight + straightDelta;

            if (_latchedBent[k])
            {
                if (vals[k] <= straightMax) _latchedBent[k] = false;
            }
            else
            {
                if (vals[k] >= bentMin) _latchedBent[k] = true;
            }

            if (_latchedBent[k]) _bits[k] = true;
        }
        CheckPatternsMaybeChoose();
    }

    private void CheckPatternsMaybeChoose()
    {
        if (!readFingerChoices) return;

        float now = Time.time;
        if (now - _lastChoiceTime < choiceCooldownSeconds) return;

        if (Matches(_bits, Pat1)) { _lastChoiceTime = now; EmitChoice(1); ResetFingerWindow(); return; }
        if (Matches(_bits, Pat2)) { _lastChoiceTime = now; EmitChoice(2); ResetFingerWindow(); return; }
        if (Matches(_bits, Pat3)) { _lastChoiceTime = now; EmitChoice(3); ResetFingerWindow(); return; }
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
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static string FixPortForDotNet(string p)
    {
        if (p.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            int num;
            if (int.TryParse(p.Substring(3), out num) && num >= 10) return @"\\.\\" + p;
        }
        return p;
    }

    private void TryOpenPort()
    {
        try
        {
            string openName = FixPortForDotNet(portName);

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

            Debug.Log($"[AccelPos] Opened {portName} at {baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open port. " + e.Message);
        }
    }

    private void ReadLoop()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;

        while (_running)
        {
            try
            {
                string line = _port.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string t = line.Trim();
                string low = t.ToLowerInvariant();

                // Finger names
                if (low == "thumb") { _main.Enqueue(() => AddFingerBentEvidence(0)); continue; }
                if (low == "index") { _main.Enqueue(() => AddFingerBentEvidence(1)); continue; }
                if (low == "middle") { _main.Enqueue(() => AddFingerBentEvidence(2)); continue; }
                if (low == "ring") { _main.Enqueue(() => AddFingerBentEvidence(3)); continue; }
                if (low == "pinkie") { _main.Enqueue(() => AddFingerBentEvidence(4)); continue; }

                // FLEX 0 or 1 bitfield
                if (low.StartsWith("flex ") || low == "flex")
                {
                    string rest = low.StartsWith("flex ") ? low.Substring(5) : "";
                    string[] tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        int[] bits = new int[5];
                        bool ok = true;
                        for (int k = 0; k < 5; k++)
                        {
                            int val;
                            ok &= int.TryParse(tokens[k], NumberStyles.Integer, inv, out val);
                            bits[k] = val;
                        }
                        if (ok)
                        {
                            int[] copy = (int[])bits.Clone();
                            _main.Enqueue(() => HandleFlexBitfield(copy));
                        }
                    }
                    continue;
                }

                // FLEXA analog
                if (low.StartsWith("flexa"))
                {
                    string rest = low.Length > 5 ? low.Substring(5).Trim() : "";
                    string[] tokens = rest.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 5)
                    {
                        int[] vals = new int[5];
                        bool ok = true;
                        for (int k = 0; k < 5; k++)
                        {
                            int val;
                            ok &= int.TryParse(tokens[k], NumberStyles.Integer, inv, out val);
                            vals[k] = val;
                        }
                        if (ok)
                        {
                            int[] copy = (int[])vals.Clone();
                            _main.Enqueue(() => HandleFlexAnalog(copy));
                        }
                    }
                    continue;
                }

                // Raw accel triple for pose pipeline
                if (readOrientationPoses)
                {
                    if (expectsCsv)
                    {
                        var p = t.Split(',');
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
}
