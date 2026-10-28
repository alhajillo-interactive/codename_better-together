// Playtest telemetry file owner: opens one CSV per match on this machine and samples the events that are not hooked
// in gameplay code (position, idle time, voice, stage). Compiles to nothing in release builds.
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FUSION_WEAVER
using System;
using System.Globalization;
using System.IO;
using System.Text;
using BetterTogether.Environment;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using BetterTogether.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Diagnostics
{
    /// <summary>
    /// Writes one CSV per match and per machine to <c>persistentDataPath/playtest</c> (see <see cref="Telemetry"/> for the
    /// row format and <c>Tools/playtest-report.py</c> for the analysis). The file opens when the countdown starts (or
    /// when a late joiner finds a match running) and closes when the lobby is shown again, the next countdown begins or
    /// the connection ends. Created automatically in the Editor and development builds, like <see cref="PerfProbe"/>.
    /// Nothing is replicated: each machine records what it sees. It also emits the events that come from the local
    /// player's input or from replicated state: <c>position_sample</c>, <c>idle</c>, <c>voice_active</c>, <c>stage_change</c>.
    /// </summary>
    public class PlaytestLogger : MonoBehaviour
    {
        [Tooltip("Seconds between position samples of the local player.")]
        [Min(0.1f)] [SerializeField] private float _positionInterval = 1f;

        [Tooltip("Seconds without moving or interacting before the player counts as idle.")]
        [Min(1f)] [SerializeField] private float _idleThreshold = 5f;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>The single logger alive in this session.</summary>
        public static PlaytestLogger Instance { get; private set; }

        private StreamWriter _writer;
        private MatchPhase _lastPhase = MatchPhase.Waiting;
        private float _nextFlush;
        private float _nextPosition;

        // Idle: time of the last movement or interaction input.
        private InputAction _move;
        private InputAction _interact;
        private InputAction _attack;
        private InputAction _jump;
        private float _lastActivity;

        private bool _talking;
        private float _talkStart;
        private MatchStage.Stage _lastStage;

        // Created at startup so every scene gets one; nothing has to be added by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAtStartup()
        {
            if (Instance == null)
                new GameObject(nameof(PlaytestLogger)).AddComponent<PlaytestLogger>();
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
        }

        private void OnEnable()
        {
            MatchManager.OnGameOver += FlushNow;
            MatchManager.OnLobby += CloseMatch;
        }

        private void OnDisable()
        {
            MatchManager.OnGameOver -= FlushNow;
            MatchManager.OnLobby -= CloseMatch;
        }

        private void Update()
        {
            var manager = MatchManager.Instance;
            var phase = manager != null ? manager.Phase : MatchPhase.Waiting;
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                OnPhaseChanged(manager, phase);
            }

            if (!Telemetry.Enabled)
                return;

            Telemetry.Tick();
            if (phase == MatchPhase.Playing)
            {
                SampleStage();
                SampleVoice();
                SampleActivity();
                SamplePosition();
            }

            if (Time.unscaledTime >= _nextFlush)
                FlushNow();
        }

        private void OnPhaseChanged(MatchManager manager, MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Countdown:
                    // A rematch goes Ended -> Countdown without passing the lobby: close the old file first.
                    CloseMatch();
                    OpenMatch(manager);
                    break;
                case MatchPhase.Playing:
                case MatchPhase.Ended:
                    if (!Telemetry.Enabled)
                        OpenMatch(manager); // Late joiner: the countdown happened before we connected.
                    if (phase == MatchPhase.Playing)
                        ResetSamples();
                    else
                        EndOfPlay();
                    break;
                default:
                    CloseMatch();
                    break;
            }
        }

        private void OpenMatch(MatchManager manager)
        {
            if (manager == null || Telemetry.Enabled)
                return;

            try
            {
                var runner = manager.Runner;
                var session = runner != null && runner.SessionInfo.IsValid ? runner.SessionInfo.Name ?? "" : "";
                var playerId = runner != null ? runner.LocalPlayer.PlayerId : -1;
                var matchId = $"{SafeFileName(session)}_{manager.StartTick}";

                var folder = Path.Combine(Application.persistentDataPath, "playtest");
                Directory.CreateDirectory(folder);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Invariant);
                var path = Path.Combine(folder, $"playtest_{stamp}_p{playerId}_{SafeFileName(LocalPlayerProfile.Name)}.csv");
                _writer = new StreamWriter(path, false, new UTF8Encoding(false));
                _writer.WriteLine(Telemetry.CsvHeader);
                _writer.Flush();

                Telemetry.DayNight = FindFirstObjectByType<DayNightCycle>();
                Telemetry.Open(_writer, matchId);
                _nextFlush = Time.unscaledTime + 1f;
                _lastStage = MatchStage.Stage.None;
                _talking = false;
                ResetSamples();
                Debug.Log($"[PlaytestLogger] Writing events to {path}", this);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlaytestLogger] Could not open the CSV: {e.Message}", this);
                CloseWriter();
            }
        }

        // The sample clocks start with the match, not with the countdown.
        private void ResetSamples()
        {
            _move = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Move") : null;
            _interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact") : null;
            _attack = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Attack") : null;
            _jump = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Jump") : null;
            _lastActivity = Time.time;
            _nextPosition = Time.time + _positionInterval;
        }

        // The match is over: report what is still running (a long idle spell, someone talking) before the file goes quiet.
        private void EndOfPlay()
        {
            if (!Telemetry.Enabled)
                return;

            ReportIdle();
            if (_talking)
                EndTalk();
        }

        private void CloseMatch()
        {
            Telemetry.Close();
            CloseWriter();
        }

        private void FlushNow()
        {
            _nextFlush = Time.unscaledTime + 1f;
            if (_writer == null)
                return;

            try
            {
                _writer.Flush();
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[PlaytestLogger] Could not flush the CSV: {e.Message}", this);
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
            catch (IOException e)
            {
                Debug.LogWarning($"[PlaytestLogger] Error closing the CSV: {e.Message}", this);
            }

            _writer = null;
        }

        private void SamplePosition()
        {
            if (Time.time < _nextPosition || PlayerCharacter.Local == null)
                return;

            _nextPosition = Time.time + _positionInterval;
            Telemetry.Begin("position_sample").Send();
        }

        // Movement or interaction input resets the idle clock; an idle spell is reported when the player acts again.
        private void SampleActivity()
        {
            var active = (_move != null && _move.ReadValue<Vector2>().sqrMagnitude > 0.0001f)
                || (_interact != null && _interact.IsPressed())
                || (_attack != null && _attack.IsPressed())
                || (_jump != null && _jump.IsPressed());
            if (!active)
                return;

            ReportIdle();
            _lastActivity = Time.time;
        }

        private void ReportIdle()
        {
            var idle = Time.time - _lastActivity;
            if (idle > _idleThreshold && PlayerCharacter.Local != null)
                Telemetry.Begin("idle").Float("duration", idle).Send();

            _lastActivity = Time.time;
        }

        // Voice: the replicated IsTalking flag of the local player; the event is written when the player stops talking.
        private void SampleVoice()
        {
            var voice = PlayerVoice.Local; // Non-null only between Spawned and Despawned, so the networked flag is safe to read.
            var talking = voice != null && voice.IsTalking;
            if (talking && !_talking)
            {
                _talking = true;
                _talkStart = Time.time;
            }
            else if (!talking && _talking)
            {
                EndTalk();
            }
        }

        private void EndTalk()
        {
            _talking = false;
            var local = PlayerCharacter.Local;
            if (local == null)
                return;

            var isGnome = local.Role == PlayerRole.Gnome;
            Telemetry.TryCamperOffset(local.transform, out var distance, out _);
            Telemetry.Begin("voice_active").Float("duration", Time.time - _talkStart).Float("dist_camper", isGnome ? distance : -1f).Send();
        }

        // The milestone is derived from the fire state on every peer, so each machine logs its own view of the change.
        private void SampleStage()
        {
            var stage = MatchStage.Current;
            if (stage == _lastStage)
                return;

            _lastStage = stage;
            if (stage != MatchStage.Stage.None)
                Telemetry.Begin("stage_change").Str("stage", stage == MatchStage.Stage.BuildFire ? "build" : "defend").Send();
        }

        private void OnApplicationQuit()
        {
            CloseMatch();
        }

        private void OnDestroy()
        {
            CloseMatch();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Turns a name into a safe file name or id fragment.</summary>
        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "sin_nombre";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ' || chars[i] == ',')
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
#endif
