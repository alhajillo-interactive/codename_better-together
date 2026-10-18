// Requires Photon Fusion 2. The FUSION_WEAVER define is added automatically when the SDK is imported,
// so this file compiles to nothing until then.
#if FUSION_WEAVER
using System.Threading.Tasks;
using Eflatun.SceneReference;
using Fusion;
using Photon.Voice.Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Creates a NetworkRunner and joins (or creates) a Photon session by room code.
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

        private NetworkRunner _runner;
        private bool _wasConnected;
        private bool _leaving;

        public NetworkRunner Runner => _runner;

        /// <summary>Room size, never above what the Fusion project config allows.</summary>
        public int MaxPlayers => Mathf.Min(_maxPlayers, NetworkProjectConfig.Global.Simulation.PlayerCount);
        public bool IsConnecting { get; private set; }
        public bool IsConnected => _runner != null && _runner.IsRunning;

        /// <summary>Why the last <see cref="JoinOrCreate"/> failed (player-facing, Spanish); null while none failed.</summary>
        public string LastError { get; private set; }

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

        public async Task<bool> JoinOrCreate(string roomCode)
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
                SessionName = string.IsNullOrWhiteSpace(roomCode) ? null : roomCode.Trim().ToUpperInvariant(),
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
                LastError = result.ShutdownReason == ShutdownReason.GameIsFull
                    ? "La sala está llena"
                    : $"No se pudo conectar ({result.ShutdownReason})";
                await Disconnect();
                return false;
            }

            _wasConnected = true;

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

        public async Task Disconnect()
        {
            if (_runner == null)
                return;

            _wasConnected = false; // An intentional disconnect must not look like a lost connection.
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
