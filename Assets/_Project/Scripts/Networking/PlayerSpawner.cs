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
        private NetworkObject _gnomePrefab;
        private NetworkObject _camperPrefab;
        private Vector3[] _spawnPoints;

        public void Init(NetworkObject gnomePrefab, NetworkObject camperPrefab, Vector3[] spawnPoints)
        {
            _gnomePrefab = gnomePrefab;
            _camperPrefab = camperPrefab;
            _spawnPoints = spawnPoints;
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (player != Runner.LocalPlayer)
                return;

            if (_gnomePrefab == null)
            {
                Debug.LogError("[PlayerSpawner] No gnome prefab assigned on ConnectionManager.");
                return;
            }

            // Temporary rule until a lobby assigns roles: the room creator (shared mode master client) is the camper.
            var prefab = Runner.IsSharedModeMasterClient && _camperPrefab != null ? _camperPrefab : _gnomePrefab;

            var position = _spawnPoints is { Length: > 0 }
                ? _spawnPoints[player.PlayerId % _spawnPoints.Length]
                : Vector3.zero;

            Runner.Spawn(prefab, position, Quaternion.identity, player);
        }
    }
}
#endif
