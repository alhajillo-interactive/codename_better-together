// Performance probe for playtests: one CSV row per second per machine (FPS, frame times, Fusion RTT, bytes/s, tick
// rates), plus an optional on-screen summary toggled with F3. Compiles to nothing in release builds.
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FUSION_WEAVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using Fusion;
using Fusion.Statistics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Diagnostics
{
    /// <summary>
    /// Samples this machine once per second and appends a row to a CSV in <c>persistentDataPath/perf</c>: frame rate,
    /// frame times, Fusion round-trip times, network bytes per second and tick rates. Created automatically in the
    /// Editor and development builds; press F3 to show the last sample on screen. Used to tell whether a slow session
    /// is caused by the network or by the frame rate.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        private const float SampleInterval = 1f;

        // Frame times kept for the p99 of one sample. Above this many frames per second, the p99 covers the last ones.
        private const int FrameTimeCapacity = 512;

        private const string CsvHeader = "sample,t_s,session,player_id,role,phase,players,objects,fps_avg,frame_ms_max,frame_ms_p99,rtt_ms,cloud_rtt_ms,in_bytes_s,out_bytes_s,tick_rate,send_rate";

        // Shown in the overlay and in the CSV when a text value is unavailable. Numeric columns use -1 instead.
        private const string Missing = "n/d";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>The single probe alive in this session.</summary>
        public static PerfProbe Instance { get; private set; }

        // Preallocated so sampling does not allocate per frame.
        private readonly float[] _frameMs = new float[FrameTimeCapacity];
        private readonly float[] _sortedFrameMs = new float[FrameTimeCapacity];
        private readonly List<NetworkObject> _networkObjects = new List<NetworkObject>(256);
        private readonly GUIContent _overlayText = new GUIContent();

        private GUIStyle _overlayStyle;
        private StreamWriter _writer;
        private bool _writerFailed;
        private bool _closed;
        private string _fileName = "";
        private NetworkRunner _loggedRunner;
        private string _sendRateText = Missing;
        private bool _overlayVisible;
        private bool _hasSample;

        // Bandwidth. Fusion's statistics snapshot is replaced on every update, so each frame adds its values here and
        // the sum is divided by the sample length when the sample closes.
        private NetworkRunner _sampleRunner;
        private FusionStatisticsManager _stats;
        private float _inSum;
        private float _outSum;
        private bool _bytesSeen;

        private float _sampleElapsed;
        private int _sampleFrames;
        private float _frameMsMax;
        private int _sampleIndex;

        // Created at startup so every scene gets one; nothing has to be added by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAtStartup()
        {
            if (Instance == null)
                new GameObject(nameof(PerfProbe)).AddComponent<PerfProbe>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // The overlay uses absolute Rects, so skip the GUILayout pass. OnGUI still runs for every IMGUI event.
            useGUILayout = false;
        }

        private void Update()
        {
            // Unscaled time: measures real frames even if something else changed Time.timeScale.
            var deltaTime = Time.unscaledDeltaTime;
            var frameMs = deltaTime * 1000f;
            _frameMs[_sampleFrames % FrameTimeCapacity] = frameMs;
            _sampleFrames++;
            _sampleElapsed += deltaTime;
            if (frameMs > _frameMsMax)
                _frameMsMax = frameMs;

            // Adds this frame's bandwidth to the sample (no allocations: the dictionary is read, not copied).
            if (_stats != null && _sampleRunner != null)
            {
                var stats = _stats.SimulationSnapshot.Stats;
                if (stats.TryGetValue(FusionStatType.InBandwidth, out var inValue))
                {
                    _inSum += inValue;
                    _bytesSeen = true;
                }

                if (stats.TryGetValue(FusionStatType.OutBandwidth, out var outValue))
                {
                    _outSum += outValue;
                    _bytesSeen = true;
                }
            }

            if (Keyboard.current != null && !FirstPersonCamera.IsTyping && Keyboard.current.f3Key.wasPressedThisFrame)
                _overlayVisible = !_overlayVisible;

            if (_sampleElapsed >= SampleInterval)
                CloseSample();
        }

        /// <summary>Computes one sample, writes a CSV row while connected and refreshes the overlay text.</summary>
        private void CloseSample()
        {
            var seconds = _sampleElapsed;
            var fps = _sampleFrames / seconds;
            var frameCount = Mathf.Min(_sampleFrames, FrameTimeCapacity);
            Array.Copy(_frameMs, _sortedFrameMs, frameCount);
            Array.Sort(_sortedFrameMs, 0, frameCount);

            // With fewer than 100 frames in a sample, p99 is the same value as the maximum.
            var frameMsP99 = frameCount > 0
                ? _sortedFrameMs[Mathf.Clamp(Mathf.CeilToInt(frameCount * 0.99f) - 1, 0, frameCount - 1)]
                : 0f;
            var frameMsMax = _frameMsMax;
            var sampleTime = Time.realtimeSinceStartup;

            _sampleElapsed = 0f;
            _sampleFrames = 0;
            _frameMsMax = 0f;
            _sampleIndex++;

            // Network and match state are read once per sample, never per frame.
            var manager = ConnectionManager.Instance;
            var runner = manager != null ? manager.Runner : null;
            var connected = runner != null && runner.IsRunning && !manager.IsConnecting;

            // Statistics belong to one runner. When the runner changes, look up its statistics and drop the old sums.
            var sampleRunner = connected ? runner : null;
            if (sampleRunner != _sampleRunner)
            {
                _sampleRunner = sampleRunner;
                _stats = sampleRunner != null && sampleRunner.TryGetFusionStatistics(out var statistics) ? statistics : null;
                _inSum = 0f;
                _outSum = 0f;
                _bytesSeen = false;
            }

            var inBytes = _bytesSeen ? _inSum / seconds : -1f;
            var outBytes = _bytesSeen ? _outSum / seconds : -1f;
            _inSum = 0f;
            _outSum = 0f;
            _bytesSeen = false;

            var rttMs = -1f;
            var cloudRttMs = -1f;
            var players = 0;
            var objects = -1;
            var session = "";
            var playerId = -1;
            var tickRateText = Missing;

            if (connected)
            {
                // Fusion returns seconds; the CSV and overlay use milliseconds.
                rttMs = (float)(runner.GetPlayerRtt(runner.LocalPlayer) * 1000.0);

                // Tuple (average, last), in seconds.
                cloudRttMs = (float)(runner.GetRttToPhotonCloud().average * 1000.0);
                players = runner.SessionInfo.PlayerCount;
                _networkObjects.Clear();
                runner.GetAllNetworkObjects(_networkObjects);
                objects = _networkObjects.Count;
                session = runner.SessionInfo.Name ?? "";
                playerId = runner.LocalPlayer.PlayerId;
                tickRateText = runner.TickRate.ToString(Invariant);

                if (runner != _loggedRunner)
                {
                    _loggedRunner = runner;
                    LogTickRates(runner);
                }

                if (_writer == null && !_writerFailed && !_closed)
                    OpenWriter(runner);
            }

            var role = connected && PlayerCharacter.Local != null ? PlayerCharacter.Local.Role.ToString() : Missing;
            var phase = connected && MatchManager.Instance != null ? MatchManager.Instance.Phase.ToString() : Missing;

            if (connected && _writer != null)
            {
                try
                {
                    _writer.WriteLine(string.Format(Invariant,
                        "{0},{1:0.0},{2},{3},{4},{5},{6},{7},{8:0.0},{9:0.00},{10:0.00},{11:0.0},{12:0.0},{13:0},{14:0},{15},{16}",
                        _sampleIndex, sampleTime, session.Replace(',', '_'), playerId, role, phase, players, objects,
                        fps, frameMsMax, frameMsP99, rttMs, cloudRttMs, inBytes, outBytes, tickRateText, _sendRateText));
                    _writer.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PerfProbe] Could not write a sample, stopping the CSV: {e.Message}", this);
                    _writerFailed = true;
                    CloseWriter();
                }
            }

            _overlayText.text = string.Format(Invariant,
                "FPS {0:0} (frame max {1:0} ms, p99 {2:0} ms)\n" +
                "Ping {3} (nube {4})\n" +
                "Bytes/s entrada {5}, salida {6}\n" +
                "Jugadores {7}, objetos {8}\n" +
                "Rol {9}, fase {10}\n" +
                "Muestra {11} en {12}",
                fps, frameMsMax, frameMsP99,
                Show(rttMs, " ms"), Show(cloudRttMs, " ms"),
                Show(inBytes, ""), Show(outBytes, ""),
                players, objects,
                role, phase,
                _sampleIndex, _writer != null ? _fileName : "sin CSV");
            _hasSample = true;
        }

        /// <summary>Logs the runner's tick rate and the resolved client and server rates. Called once per runner.</summary>
        private void LogTickRates(NetworkRunner runner)
        {
            try
            {
                var selection = runner.Config.Simulation.TickRateSelection;
                if (selection.Client > 0)
                {
                    var rates = TickRate.Resolve(selection);
                    _sendRateText = rates.ClientSend.ToString(Invariant);
                    Debug.Log($"[PerfProbe] Connected: runner tick {runner.TickRate}/s; client tick {rates.Client} send {rates.ClientSend}; server tick {rates.Server} send {rates.ServerSend}. Change: NetworkProjectConfig.fusion > Simulation > Tick Rate.", this);
                }
                else
                {
                    _sendRateText = "default";
                    Debug.Log($"[PerfProbe] Connected: runner tick {runner.TickRate}/s; Tick Rate is 'Default' (Shared): the send rate is shown in the NetworkProjectConfig inspector, 'Shared Mode' row.", this);
                }
            }
            catch (Exception e)
            {
                _sendRateText = Missing;
                Debug.LogWarning($"[PerfProbe] Could not resolve the tick rates: {e.Message}", this);
            }
        }

        /// <summary>Opens the CSV with its header. Called once, at the first connected sample.</summary>
        private void OpenWriter(NetworkRunner runner)
        {
            try
            {
                var folder = Path.Combine(Application.persistentDataPath, "perf");
                Directory.CreateDirectory(folder);

                // One file per application run; reconnections append rows with a different session.
                // The player id keeps two instances that connect in the same second (Multiplayer Play Mode) apart.
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Invariant);
                _fileName = $"perf_{stamp}_p{runner.LocalPlayer.PlayerId}_{SafeFileName(LocalPlayerProfile.Name)}.csv";
                _writer = new StreamWriter(Path.Combine(folder, _fileName), false, new UTF8Encoding(false));
                _writer.WriteLine(CsvHeader);
                _writer.Flush();
                Debug.Log($"[PerfProbe] Writing samples to {Path.Combine(folder, _fileName)}", this);
            }
            catch (Exception e)
            {
                _writerFailed = true;
                Debug.LogError($"[PerfProbe] Could not open the CSV: {e.Message}", this);
            }
        }

        private void CloseWriter()
        {
            if (_writer == null)
                return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PerfProbe] Error closing the CSV: {e.Message}", this);
            }

            _writer = null;
        }

        // Quitting the application closes the file for good. Leaving to the menu does not: the probe survives the scene change.
        private void OnApplicationQuit()
        {
            _closed = true;
            CloseWriter();
        }

        private void OnDestroy()
        {
            _closed = true;
            CloseWriter();
            if (Instance == this)
                Instance = null;
        }

        private void OnGUI()
        {
            if (!_overlayVisible || !_hasSample)
                return;

            _overlayStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6),
            };

            // Bottom right, so it does not cover the top-right role panel (F1).
            const float width = 380f;
            const float height = 130f;
            GUI.Label(new Rect(Screen.width - width - 10f, Screen.height - height - 10f, width, height), _overlayText, _overlayStyle);
        }

        /// <summary>Value for the overlay: the number with its unit, or <see cref="Missing"/> when it is -1.</summary>
        private static string Show(float value, string unit) =>
            value < 0f ? Missing : string.Format(Invariant, "{0:0}{1}", value, unit);

        /// <summary>Turns a player name into a safe file name fragment.</summary>
        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "sin_nombre";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
#endif
