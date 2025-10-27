using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
using System.Linq;
#endif

public class MiniGame2_3Glove : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 9600;
    [Tooltip("Your Arduino prints finger names and/or 'FLEX a b c d e' lines")]
    public bool expectsCsv = false; // unused here; kept for parity

    [Header("Fist Detection")]
    [Tooltip("Window length (seconds) that starts at the FIRST finger bend")]
    public float fistWindowSeconds = 1.5f;
    [Tooltip("Cooldown between two fist detections")]
    public float fistCooldownSeconds = 0.75f;

    [Header("Target (BeehiveMiniGame3)")]
    [Tooltip("Assign the object that has BeehiveMiniGame3 on it")]
    public GameObject beehiveObject; // we’ll SendMessage("SpawnRock") to this

    [Header("Debug")]
    public bool logRaw = true;
    public int rawLogMax = 200;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    public bool useDtr = true;
    public bool useRts = true;
    private SerialPort _port;
#endif

    // ----- threading bridge -----
    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    private Thread _thread;
    private volatile bool _running;

    // ----- fist state -----
    private static readonly string[] AllFingers = { "thumb", "index", "middle", "ring", "pinkie" };
    private bool _fistWindowActive = false;
    private float _fistWindowStart = 0f;
    private readonly HashSet<string> _fingersSeen = new HashSet<string>();
    private float _lastFistTime = -999f;

    private int _rawCount = 0;

    void Start()
    {
        if (beehiveObject == null)
            Debug.LogWarning("[MiniGame2_3Glove] No beehiveObject assigned. Drag your BeehiveMiniGame3 GameObject here.");

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
        while (_main.TryDequeue(out var act))
            act?.Invoke();

        // expire fist window if running
        if (_fistWindowActive && (Time.time - _fistWindowStart > fistWindowSeconds))
            ResetFistWindow();
    }

    // ====== Fist helpers ======
    private void StartFistWindow(string firstFinger)
    {
        _fistWindowActive = true;
        _fistWindowStart = Time.time;
        _fingersSeen.Clear();
        _fingersSeen.Add(firstFinger);
    }

    private void AddFingerAndTryComplete(string finger)
    {
        if (!_fistWindowActive) StartFistWindow(finger);
        else _fingersSeen.Add(finger);

        if (_fingersSeen.Count == 5) // all fingers seen within window
        {
            FireFistIfCooldown();
            ResetFistWindow();
        }
    }

    private void FireFistIfCooldown()
    {
        float now = Time.time;
        if (now - _lastFistTime < fistCooldownSeconds) return;

        _lastFistTime = now;
        Debug.Log("SELECT (FIST)");

        // Call SpawnRock() on BeehiveMiniGame3 holder without modifying that script
        if (beehiveObject != null)
            beehiveObject.SendMessage("SpawnRock", SendMessageOptions.DontRequireReceiver);
        else
            Debug.LogWarning("[MiniGame2_3Glove] beehiveObject not set; cannot SendMessage(\"SpawnRock\").");
    }

    private void ResetFistWindow()
    {
        _fistWindowActive = false;
        _fingersSeen.Clear();
        _fistWindowStart = 0f;
    }

    // FLEX bitfield like: FLEX 1 1 1 1 1
    private void HandleFlexBitfield(int[] bits)
    {
        bool allBent = true;
        for (int i = 0; i < 5; i++) if (bits[i] == 0) { allBent = false; break; }

        if (allBent)
        {
            FireFistIfCooldown();
            ResetFistWindow();
            return;
        }

        // otherwise treat each 1 as a bent finger inside the window
        for (int i = 0; i < 5; i++)
        {
            if (bits[i] == 1)
            {
                string f = AllFingers[i];
                AddFingerAndTryComplete(f);
            }
        }
    }

    // ====== Serial ======
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

            Debug.Log($"[MiniGame2_3Glove] Opened {chosen} @ {baudRate}");
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
                    if (isFinger || isFlex)
                    {
                        _main.Enqueue(() => Debug.Log($"[RAW] {tline}"));
                        _rawCount++;
                    }
                }

                // Finger name lines
                if (low == "thumb" || low == "index" || low == "middle" || low == "ring" || low == "pinkie")
                {
                    string f = low;
                    _main.Enqueue(() => AddFingerAndTryComplete(f));
                    continue;
                }

                // FLEX bitfield: "FLEX 1 1 1 1 1"
                if (low.StartsWith("flex"))
                {
                    string rest = low.Substring(4).Trim();
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

                // ignore accel lines entirely for this mini-game
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
