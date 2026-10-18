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

        [SerializeField] private int _maxPlayers = 4;
        [Tooltip("Gameplay scene to load after connecting. Must be in Build Settings.")]
        [SerializeField] private SceneReference _gameplayScene;

        private NetworkRunner _runner;

        public NetworkRunner Runner => _runner;
        public bool IsConnecting { get; private set; }
        public bool IsConnected => _runner != null && _runner.IsRunning;

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

        public async Task<bool> JoinOrCreate(string roomCode)
        {
            if (IsConnecting || IsConnected)
                return false;

            // By path, not build index: indices differ between machines whose scene lists differ.
            if (!PathSceneManager.TryGetSceneRef(_gameplayScene, out var gameplaySceneRef, out var sceneError))
            {
                Debug.LogError($"[ConnectionManager] Gameplay Scene: {sceneError}", this);
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

            // CLAUDE TODO: use the scene reference _gameplayScene in this game object to load said scene so we can easily reference it on the unity editor
            var sceneManager = runnerObject.AddComponent<PathSceneManager>();

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(gameplaySceneRef, LoadSceneMode.Single);

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = string.IsNullOrWhiteSpace(roomCode) ? null : roomCode.Trim().ToUpperInvariant(),
                PlayerCount = _maxPlayers,
                Scene = sceneInfo,
                SceneManager = sceneManager,
            });

            IsConnecting = false;

            if (!result.Ok)
            {
                Debug.LogError($"[ConnectionManager] Failed to start: {result.ShutdownReason}");
                await Disconnect();
                return false;
            }

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

            if (!PathSceneManager.TryGetSceneRef(scene, out var sceneRef, out var error))
            {
                Debug.LogError($"[ConnectionManager] {error}", this);
                return false;
            }

            _runner.LoadScene(sceneRef, LoadSceneMode.Single);
            return true;
        }

        public async Task Disconnect()
        {
            if (_runner == null)
                return;

            var runner = _runner;
            _runner = null;
            await runner.Shutdown(); // Also destroys the runner's GameObject.
        }
    }
}
#endif
