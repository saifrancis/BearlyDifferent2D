using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
#endif

public class AccelerometerPos : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 115200;
    [Tooltip("True for ax,ay,az lines. False for labeled lines like X: aY: bZ: cm/s^2")]
    public bool expectsCsv = false;

    [Header("Templates in m per s squared")]
    public Vector3 downTpl = new Vector3(-0.16f, 1.61f, 10.32f);
    public Vector3 neutralTpl = new Vector3(-9.53f, 2.12f, 1.73f);
    public Vector3 leftTpl = new Vector3(0.27f, 4.00f, 9.92f);
    public Vector3 rightTpl = new Vector3(-1.53f, -9.02f, 0.16f);

    [Header("Matching")]
    [Tooltip("Allowed absolute difference on each axis")]
    public float tolerance = 1.2f;
    [Range(0f, 1f)] public float alpha = 0.35f;
    [Tooltip("Time the pose must be held to fire")]
    public float dwellTime = 0.12f;

    [Header("Neutral rearm")]
    [Tooltip("Hold NEUTRAL for this time to rearm after any trigger")]
    public float neutralRearmDwell = 0.25f;

    public event Action<string> OnPose;   // emits "DOWN", "NEUTRAL", "LEFT", "RIGHT"

    private Vector3 _filt;
    private string _lastEmitted = null;

    private string _candidate = null;
    private float _candidateStart = 0f;

    private bool _requireNeutral = false;
    private float _neutralSeenStart = 0f;

    private volatile bool _running;
    private Thread _thread;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private SerialPort _port;
#endif

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
        Debug.LogWarning("Serial works on Windows Editor and Windows Standalone only");
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
        if (!_hasSample) return;

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

        if (now - _candidateStart >= dwellTime)
        {
            if (_lastEmitted != match)
            {
                _lastEmitted = match;
                OnPose?.Invoke(match);
                Debug.Log(match);

                _requireNeutral = true;
                _neutralSeenStart = 0f;
            }
        }
    }

    private string ClassifyByWindow(Vector3 v)
    {
        bool isDown = Within(v, downTpl, tolerance);
        bool isNeutral = Within(v, neutralTpl, tolerance);
        bool isLeft = Within(v, leftTpl, tolerance);
        bool isRight = Within(v, rightTpl, tolerance);

        if (!isDown && !isNeutral && !isLeft && !isRight) return null;

        float bestErr = float.MaxValue;
        string best = null;

        if (isDown) TryBest(ref best, ref bestErr, "DOWN", SumAbsError(v, downTpl));
        if (isNeutral) TryBest(ref best, ref bestErr, "NEUTRAL", SumAbsError(v, neutralTpl));
        if (isLeft) TryBest(ref best, ref bestErr, "LEFT", SumAbsError(v, leftTpl));
        if (isRight) TryBest(ref best, ref bestErr, "RIGHT", SumAbsError(v, rightTpl));

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

    private static void TryBest(ref string label, ref float err, string candidate, float candErr)
    {
        if (candErr < err)
        {
            err = candErr;
            label = candidate;
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private void TryOpenPort()
    {
        try
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
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

            Debug.Log($"Serial opened on {portName} at {baudRate}");
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

                if (expectsCsv)
                {
                    var p = line.Split(',');
                    if (p.Length >= 3
                        && float.TryParse(p[0], NumberStyles.Float, inv, out float ax)
                        && float.TryParse(p[1], NumberStyles.Float, inv, out float ay)
                        && float.TryParse(p[2], NumberStyles.Float, inv, out float az))
                    {
                        _ax = ax; _ay = ay; _az = az;
                        _hasSample = true;
                    }
                }
                else
                {
                    if (TryParseTriple(line, out float ax, out float ay, out float az))
                    {
                        _ax = ax; _ay = ay; _az = az;
                        _hasSample = true;
                    }
                }
            }
            catch (TimeoutException)
            {
            }
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
