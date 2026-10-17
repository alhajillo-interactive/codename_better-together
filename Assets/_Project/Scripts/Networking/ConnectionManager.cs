// Requires Photon Fusion 2. The FUSION_WEAVER define is added automatically when the SDK is imported,
// so this file compiles to nothing until then.
#if FUSION_WEAVER
using System.Threading.Tasks;
using Fusion;
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

        [SerializeField] private NetworkObject _playerPrefab;
        [SerializeField] private Vector3[] _spawnPoints = { Vector3.zero };
        [SerializeField] private int _maxPlayers = 4;
        [Tooltip("Build index of the gameplay scene to load after connecting. -1 = stay in the current scene.")]
        [SerializeField] private int _gameplaySceneBuildIndex = -1;

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
            runnerObject.AddComponent<PlayerSpawner>().Init(_playerPrefab, _spawnPoints);
            var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

            var sceneIndex = _gameplaySceneBuildIndex >= 0
                ? _gameplaySceneBuildIndex
                : SceneManager.GetActiveScene().buildIndex;
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
