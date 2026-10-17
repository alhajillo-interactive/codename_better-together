#if FUSION_WEAVER
using Fusion;
using UnityEngine;

namespace BetterTogether.Networking
{
    /// <summary>
    /// In Shared mode each client spawns its own player when it joins.
    /// Added by ConnectionManager on the same GameObject as the NetworkRunner so it receives runner callbacks.
    /// </summary>
    public class PlayerSpawner : SimulationBehaviour, IPlayerJoined
    {
        private NetworkObject _playerPrefab;
        private Vector3[] _spawnPoints;

        public void Init(NetworkObject playerPrefab, Vector3[] spawnPoints)
        {
            _playerPrefab = playerPrefab;
            _spawnPoints = spawnPoints;
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (player != Runner.LocalPlayer)
                return;

            if (_playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] No player prefab assigned on ConnectionManager.");
                return;
            }

            var position = _spawnPoints is { Length: > 0 }
                ? _spawnPoints[player.PlayerId % _spawnPoints.Length]
                : Vector3.zero;

            Runner.Spawn(_playerPrefab, position, Quaternion.identity, player);
        }
    }
}
#endif
