// Requires Photon Fusion 2. The FUSION_WEAVER define is added automatically when the SDK is imported,
// so this file compiles to nothing until then.
#if FUSION_WEAVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterTogether.Diagnostics;
using BetterTogether.Match;
using Eflatun.SceneReference;
using Fusion;
using Photon.Voice.Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Creates a NetworkRunner and joins (or creates) a Photon session by room code, or a public one (create, pick from
    /// the list, quick join).
    /// Uses Shared mode: no host/dedicated server needed, each client owns its own player.
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        [Tooltip("Players per room: one camper, the rest gnomes. Fusion caps it at the Player Count of NetworkProjectConfig.")]
        [SerializeField, Range(2, 10)] private int _maxPlayers = 8;
        [Tooltip("Gameplay scene to load after connecting. Must be in Build Settings.")]
        [SerializeField] private SceneReference _gameplayScene;
        [Tooltip("Scene to go back to when leaving the room or losing the connection. Optional; must be in Build Settings.")]
        [SerializeField] private SceneReference _menuScene;
        [Tooltip("Seconds to wait for the session to start before giving up.")]
        [SerializeField, Min(1f)] private float _connectTimeout = 15f;

        /// <summary>Session property that marks a room as public (also the filter used by quick join).</summary>
        private const string PublicKey = "pub";

        /// <summary>Session property with the room state: <see cref="PhaseLobby"/> or <see cref="PhaseGame"/>.</summary>
        public const string PhaseKey = "fase";
        public const string PhaseLobby = "lobby";
        public const string PhaseGame = "juego";

        private NetworkRunner _runner;
        private bool _wasConnected;
        private bool _leaving;

        // Public rooms: only the Shared master client publishes the phase; late join stays allowed.
        private bool _isPublic;
        private readonly Dictionary<string, SessionProperty> _publicProps = new Dictionary<string, SessionProperty>(2);

        // Lobby browsing runner (separate from the game runner; shut down before entering a room).
        private NetworkRunner _browseRunner;
        private SessionListListener _browseListener;
        private bool _browseStarting;

        // Matchmaking telemetry waits for the match file to open (the menu has no file), then is sent once.
        private string _pendingMode;
        private float _pendingSeconds;

        public NetworkRunner Runner => _runner;

        /// <summary>Room size, never above what the Fusion project config allows.</summary>
        public int MaxPlayers => Mathf.Min(_maxPlayers, NetworkProjectConfig.Global.Simulation.PlayerCount);
        public bool IsConnecting { get; private set; }
        public bool IsConnected => _runner != null && _runner.IsRunning;

        /// <summary>Why the last <see cref="JoinOrCreate"/> failed (player-facing, Spanish); null while none failed.</summary>
        public string LastError { get; private set; }

        /// <summary>True while a lobby browsing runner is up.</summary>
        public bool IsBrowsing => _browseRunner != null;

        /// <summary>Public sessions of the last list received while browsing (reused list; empty when not browsing).</summary>
        public IReadOnlyList<SessionInfo> Sessions => _browseListener != null ? _browseListener.Sessions : Array.Empty<SessionInfo>();

        /// <summary>Raised each time the public session list changes while browsing.</summary>
        public event Action<IReadOnlyList<SessionInfo>> SessionsUpdated;

        /// <summary>Message the main menu shows once when it loads (e.g. after losing the connection), then clears.</summary>
        public static string PendingMenuMessage { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            MatchManager.OnGameStarted += OnMatchStarted;
            MatchManager.OnLobby += OnMatchLobby;
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            MatchManager.OnGameStarted -= OnMatchStarted;
            MatchManager.OnLobby -= OnMatchLobby;
        }

        private void Update()
        {
            if (IsConnected)
            {
                _wasConnected = true;
            }
            else if (_wasConnected && !IsConnecting && !_leaving)
            {
                // The runner stopped without us asking (kicked, network drop, session closed).
                _wasConnected = false;
                _ = ReturnToMenu("Se perdió la conexión con la sala.");
            }
        }

        /// <summary>Private room by code: invisible in the public list.</summary>
        public Task<bool> JoinOrCreate(string roomCode)
        {
            var name = string.IsNullOrWhiteSpace(roomCode) ? null : roomCode.Trim().ToUpperInvariant();
            return StartSession("code", name, false, null);
        }

        /// <summary>Creates a visible room with a readable random name.</summary>
        public Task<bool> CreatePublic()
        {
            return StartSession("public", $"Campamento {UnityEngine.Random.Range(1000, 10000)}", true, PublicProps(PhaseLobby));
        }

        /// <summary>Joins a room picked from <see cref="Sessions"/>.</summary>
        public Task<bool> JoinPublic(SessionInfo session)
        {
            if (session == null || !session.IsValid)
            {
                LastError = "La sala ya no existe";
                return Task.FromResult(false);
            }

            if (session.PlayerCount >= session.MaxPlayers)
            {
                LastError = "La sala está llena";
                return Task.FromResult(false);
            }

            return StartSession("public", session.Name, true, null);
        }

        /// <summary>
        /// Joins any open public room with space (Fusion random matching, filtered by the public property); creates a
        /// new public room if there is none.
        /// </summary>
        public Task<bool> QuickJoin()
        {
            return StartSession("quick", null, true, new Dictionary<string, SessionProperty> { [PublicKey] = 1 });
        }

        private static Dictionary<string, SessionProperty> PublicProps(string phase)
        {
            return new Dictionary<string, SessionProperty> { [PublicKey] = 1, [PhaseKey] = phase };
        }

        private async Task<bool> StartSession(string mode, string sessionName, bool visible, Dictionary<string, SessionProperty> properties)
        {
            if (IsConnecting || IsConnected || _leaving)
                return false;

            LastError = null;

            if (!TryGetSceneRef(_gameplayScene, out var gameplaySceneRef, out var sceneError))
            {
                Debug.LogError($"[ConnectionManager] Gameplay Scene: {sceneError}", this);
                LastError = $"Escena de juego no válida: {sceneError}";
                return false;
            }

            IsConnecting = true;
            var startedAt = Time.realtimeSinceStartup;

            // The browsing runner must be gone before the game runner starts.
            await StopBrowsing();

            // A runner can't be reused after shutdown, so each session gets its own GameObject.
            var runnerObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(runnerObject);
            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            // Joins the matching voice room automatically; players' VoiceNetworkObject uses it.
            runnerObject.AddComponent<FusionVoiceClient>();

            // Path-based scene refs; see PathSceneManager for why the default (index-based) one must not be used.
            var sceneManager = runnerObject.AddComponent<PathSceneManager>();

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(gameplaySceneRef, LoadSceneMode.Single);

            var startTask = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                IsVisible = visible,
                IsOpen = true,
                SessionProperties = properties,
                PlayerCount = MaxPlayers,
                Scene = sceneInfo,
                SceneManager = sceneManager,
            });

            var finished = await Task.WhenAny(startTask, Task.Delay(Mathf.RoundToInt(_connectTimeout * 1000f)));
            if (finished != startTask)
            {
                Debug.LogError($"[ConnectionManager] Timed out after {_connectTimeout:0}s starting the session.");
                LastError = "La conexión tardó demasiado.";
                // The abandoned start call fails once the runner shuts down; observe it so it isn't reported as unobserved.
                _ = startTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                IsConnecting = false;
                await Disconnect();
                return false;
            }

            var result = await startTask;
            IsConnecting = false;

            if (!result.Ok)
            {
                Debug.LogError($"[ConnectionManager] Failed to start: {result.ShutdownReason}");
                LastError = result.ShutdownReason switch
                {
                    ShutdownReason.GameIsFull => "La sala está llena",
                    ShutdownReason.GameNotFound => "La sala ya no existe",
                    _ => $"No se pudo conectar ({result.ShutdownReason})",
                };
                await Disconnect();
                return false;
            }

            _wasConnected = true;
            _isPublic = visible;
            _pendingMode = mode;
            _pendingSeconds = Time.realtimeSinceStartup - startedAt;
            if (_isPublic)
                PublishPhase(MatchManager.Instance != null && MatchManager.Instance.Phase == MatchPhase.Playing);

            Debug.Log($"[ConnectionManager] Connected to session '{_runner.SessionInfo.Name}' in region {_runner.SessionInfo.Region}");
            return true;
        }

        /// <summary>
        /// Moves every player in the session to another scene. Only the scene authority (Shared mode master client)
        /// may do this; other clients follow automatically.
        /// </summary>
        public bool LoadNetworkScene(SceneReference scene)
        {
            if (!IsConnected || !_runner.IsSceneAuthority)
            {
                Debug.LogWarning("[ConnectionManager] Only the scene authority can change the networked scene.", this);
                return false;
            }

            if (!TryGetSceneRef(scene, out var sceneRef, out var error))
            {
                Debug.LogError($"[ConnectionManager] {error}", this);
                return false;
            }

            _runner.LoadScene(sceneRef, LoadSceneMode.Single);
            return true;
        }

        /// <summary>
        /// Resolves a scene picked in the inspector to the networked scene to load. It is sent by asset path, never by
        /// Build Settings index: the index depends on each machine's scene list (see <see cref="PathSceneManager"/>).
        /// </summary>
        private static bool TryGetSceneRef(SceneReference scene, out SceneRef sceneRef, out string error)
        {
            sceneRef = SceneRef.None;

            if (scene == null || scene.UnsafeReason != SceneReferenceUnsafeReason.None)
            {
                error = scene == null
                    ? "No scene assigned."
                    : $"The scene can't be loaded ({scene.UnsafeReason}); add it to the Build Settings scene list.";
                return false;
            }

            sceneRef = SceneRef.FromPath(scene.Path);
            error = null;
            return true;
        }

        #region Public rooms

        /// <summary>
        /// Starts a runner in the shared session lobby so <see cref="SessionsUpdated"/> reports the public rooms.
        /// Does nothing while connecting or inside a room. Call <see cref="StopBrowsing"/> when the list is no longer shown.
        /// </summary>
        public async Task<bool> StartBrowsing()
        {
            if (_browseRunner != null || _browseStarting || IsConnecting || IsConnected || _leaving)
                return IsBrowsing;

            _browseStarting = true;
            LastError = null;

            var runnerObject = new GameObject("SessionBrowser");
            DontDestroyOnLoad(runnerObject);
            var runner = runnerObject.AddComponent<NetworkRunner>();
            var listener = new SessionListListener();
            listener.Updated += OnBrowseListUpdated;
            runner.AddCallbacks(listener);
            _browseRunner = runner;
            _browseListener = listener;

            var joinTask = runner.JoinSessionLobby(SessionLobby.Shared);
            var finished = await Task.WhenAny(joinTask, Task.Delay(Mathf.RoundToInt(_connectTimeout * 1000f)));
            _browseStarting = false;

            var ok = false;
            if (finished == joinTask)
            {
                var result = await joinTask;
                ok = result.Ok;
                if (!ok)
                    Debug.LogWarning($"[ConnectionManager] Could not join the session lobby: {result.ShutdownReason}");
            }
            else
            {
                _ = joinTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }

            // Stopped while connecting (the menu closed or a room was joined): nothing left to keep.
            if (_browseRunner != runner)
            {
                await ShutdownBrowseRunner(runner, listener);
                return false;
            }

            if (!ok)
            {
                LastError = "No se pudo cargar la lista de partidas";
                await StopBrowsing();
            }

            return ok;
        }

        /// <summary>Shuts the browsing runner down. Safe to call at any time.</summary>
        public async Task StopBrowsing()
        {
            var runner = _browseRunner;
            if (runner == null)
                return;

            var listener = _browseListener;
            _browseRunner = null;
            _browseListener = null;
            await ShutdownBrowseRunner(runner, listener);
        }

        private async Task ShutdownBrowseRunner(NetworkRunner runner, SessionListListener listener)
        {
            if (listener != null)
                listener.Updated -= OnBrowseListUpdated;

            if (runner != null)
                await runner.Shutdown(); // Also destroys the runner's GameObject.
        }

        private void OnBrowseListUpdated(IReadOnlyList<SessionInfo> sessions) => SessionsUpdated?.Invoke(sessions);

        /// <summary>Reads the room state of a listed session; a room that never published it counts as lobby.</summary>
        public static bool IsInGame(SessionInfo session)
        {
            if (session == null || session.Properties == null || !session.Properties.TryGetValue(PhaseKey, out var phase))
                return false;

            return phase.PropertyValue?.ToString() == PhaseGame;
        }

        private void OnMatchStarted()
        {
            PublishPhase(true);

            if (_pendingMode != null && Telemetry.Enabled)
            {
                Telemetry.Begin("matchmaking").Str("mode", _pendingMode).Float("seconds", _pendingSeconds).Send();
                _pendingMode = null;
            }
        }

        private void OnMatchLobby() => PublishPhase(false);

        /// <summary>
        /// Updates the room property that tells the list whether a match is running. Only the Shared master client does
        /// it (it migrates with the master); the room stays open, so late join keeps working.
        /// </summary>
        private void PublishPhase(bool inGame)
        {
            if (!_isPublic || _runner == null || !_runner.IsRunning || !_runner.IsSharedModeMasterClient)
                return;

            var value = inGame ? PhaseGame : PhaseLobby;
            var info = _runner.SessionInfo;
            if (info != null && info.Properties != null && info.Properties.TryGetValue(PhaseKey, out var current)
                && current.PropertyValue?.ToString() == value)
                return;

            _publicProps[PublicKey] = 1;
            _publicProps[PhaseKey] = value;
            info?.UpdateCustomProperties(_publicProps);
        }

        #endregion

        public async Task Disconnect()
        {
            if (_runner == null)
                return;

            _wasConnected = false; // An intentional disconnect must not look like a lost connection.
            _isPublic = false;
            _pendingMode = null;
            var runner = _runner;
            _runner = null;
            await runner.Shutdown(); // Also destroys the runner's GameObject.
        }

        /// <summary>
        /// Leaves the room and goes back to the menu scene (if one is assigned), leaving <paramref name="message"/>
        /// for the menu to show once.
        /// </summary>
        public async Task ReturnToMenu(string message = null)
        {
            if (_leaving)
                return;

            _leaving = true;
            try
            {
                PendingMenuMessage = message;
                await Disconnect();

                if (_menuScene != null && _menuScene.UnsafeReason == SceneReferenceUnsafeReason.None)
                    SceneManager.LoadScene(_menuScene.Path);
                else
                    Debug.LogWarning("[ConnectionManager] No valid Menu Scene assigned; staying in the current scene.", this);

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            finally
            {
                _leaving = false;
            }
        }
    }
}
#endif
