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

public class MiniGame2_2Glove : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int baudRate = 9600;
    [Tooltip("Arduino prints finger names and/or 'FLEX a b c d e' lines")]
    public bool expectsCsv = false; // kept for parity, not used

    [Header("Fist detection")]
    [Tooltip("Window length (seconds) that starts at the FIRST finger bend")]
    public float fistWindowSeconds = 1.5f;
    [Tooltip("Cooldown between two fist detections")]
    public float fistCooldownSeconds = 0.75f;

    [Header("Target mini-game")]
    public SquirrelMiniGame squirrelGame;    // optional; auto-found if null

    [Header("Debug")]
    public bool logRaw = true;
    public int rawLogMax = 200;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [Header("Serial (advanced)")]
    public bool useDtr = true;
    public bool useRts = true;
    private SerialPort _port;
#endif

    // ---------- threading ----------
    private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    private Thread _thread;
    private volatile bool _running;

    // ---------- fist window ----------
    private static readonly string[] AllFingers = { "thumb", "index", "middle", "ring", "pinkie" };
    private bool _fistWindowActive = false;
    private float _fistWindowStart = 0f;
    private readonly HashSet<string> _fingersSeen = new HashSet<string>();
    private float _lastFistTime = -999f;

    private int _rawCount = 0;

    private void Start()
    {
        // Auto-find the SquirrelMiniGame in the scene if not assigned
        if (squirrelGame == null)
        {
            squirrelGame = FindObjectOfType<SquirrelMiniGame>(true);
            if (squirrelGame == null)
                Debug.LogWarning("[MiniGame2_2Glove] No SquirrelMiniGame found in scene.");
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        TryOpenPort();
#else
        Debug.LogWarning("Serial runs on Windows Editor/Standalone only.");
#endif
    }

    private void OnDestroy()
    {
        _running = false;
        try { _thread?.Join(200); } catch { }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
        try { _port?.Dispose(); } catch { }
        _port = null;
#endif
    }

    private void Update()
    {
        while (_main.TryDequeue(out var act))
            act?.Invoke();

        // expire fist window if running
        if (_fistWindowActive)
        {
            float now = Time.time;
            if (now - _fistWindowStart > fistWindowSeconds)
                ResetFistWindow();
        }
    }

    // ================= FIST HELPERS =================
    private void StartFistWindow(string firstFinger)
    {
        _fistWindowActive = true;
        _fistWindowStart = Time.time;
        _fingersSeen.Clear();
        _fingersSeen.Add(firstFinger);
    }

    private void AddFingerAndTryComplete(string finger)
    {
        if (!_fistWindowActive)
            StartFistWindow(finger);
        else
            _fingersSeen.Add(finger);

        if (_fingersSeen.Count == 5)
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

        // Same effect as pressing Space in the Squirrel mini-game
        if (squirrelGame != null)
        {
            try { squirrelGame.OnFist(); }
            catch (Exception e) { Debug.LogException(e); }
        }
        else
        {
            Debug.LogWarning("[MiniGame2_2Glove] Fist detected but no SquirrelMiniGame assigned/found.");
        }
    }

    private void ResetFistWindow()
    {
        _fistWindowActive = false;
        _fingersSeen.Clear();
        _fistWindowStart = 0f;
    }

    // FLEX bitfield like "FLEX 1 1 1 1 1"
    private void HandleFlexBitfield(int[] bits)
    {
        // If all ones, trigger immediately (respect cooldown)
        bool allBent = true;
        for (int i = 0; i < 5; i++) if (bits[i] == 0) { allBent = false; break; }

        if (allBent)
        {
            FireFistIfCooldown();
            ResetFistWindow();
            return;
        }

        // Otherwise, treat each 1 as a finger seen (within window)
        for (int i = 0; i < 5; i++)
        {
            if (bits[i] == 1)
            {
                string f = AllFingers[i];
                AddFingerAndTryComplete(f);
            }
        }
    }

    // ================= SERIAL =================
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

            Debug.Log($"[MiniGame2_2Glove] Opened {chosen} @ {baudRate}");
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
                    string f = low; // capture
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

                // Ignore accel lines for this mini-game
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
