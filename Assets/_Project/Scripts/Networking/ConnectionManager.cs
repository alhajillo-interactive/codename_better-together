// Requires Photon Fusion 2. The FUSION_WEAVER define is added automatically when the SDK is imported,
// so this file compiles to nothing until then.
#if FUSION_WEAVER
using System.Threading.Tasks;
using Eflatun.SceneReference;
using Fusion;
using Photon.Voice.Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Creates a NetworkRunner and joins (or creates) a Photon session by room code.
    /// Uses Shared mode: no host/dedicated server needed, each client owns its own player.
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        [FormerlySerializedAs("_playerPrefab")]
        [SerializeField] private NetworkObject _gnomePrefab;
        [Tooltip("Optional. Spawned for the room creator; if empty everyone spawns as a gnome.")]
        [SerializeField] private NetworkObject _camperPrefab;
        [SerializeField] private Vector3[] _spawnPoints = { Vector3.zero };
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

            IsConnecting = true;

            // A runner can't be reused after shutdown, so each session gets its own GameObject.
            var runnerObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(runnerObject);
            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            // Joins the matching voice room automatically; players' VoiceNetworkObject uses it.
            runnerObject.AddComponent<FusionVoiceClient>();
            runnerObject.AddComponent<PlayerSpawner>().Init(_gnomePrefab, _camperPrefab, _spawnPoints);
            var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

            var sceneIndex = _gameplayScene.BuildIndex;
            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);

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
