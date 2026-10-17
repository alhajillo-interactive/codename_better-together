#if FUSION_WEAVER
using BetterTogether.Interaction;
using BetterTogether.Player;
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

        /// <summary>
        /// Replaces the local player's character with the prefab for <paramref name="role"/>, keeping its position.
        /// Must run on the character's owner.
        /// </summary>
        public void ChangeRole(PlayerCharacter current, PlayerRole role)
        {
            var prefab = role == PlayerRole.Camper ? _camperPrefab : _gnomePrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[PlayerSpawner] No prefab assigned for role {role} on ConnectionManager.");
                return;
            }

            if (current.TryGetComponent<PlayerInteractor>(out var interactor) && interactor.Carried != null)
                interactor.Carried.Drop();

            var currentTransform = current.transform;
            var position = currentTransform.position;
            var rotation = currentTransform.rotation;

            Runner.Despawn(current.Object);
            Runner.Spawn(prefab, position, rotation, Runner.LocalPlayer);
        }
    }
}
#endif
