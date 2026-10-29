using BetterTogether.Diagnostics;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Scene object that spawns the players. In Shared mode each client spawns its own character: a gnome (the lobby
    /// avatar) when it joins, then the role chosen by the match (see MatchManager) when the countdown starts.
    /// </summary>
    public class PlayerSpawner : NetworkBehaviour, IPlayerJoined
    {
        [SerializeField] private NetworkObject _gnomePrefab;
        [SerializeField] private NetworkObject _camperPrefab;
        [Tooltip("Children of this object. The player with PlayerId n starts at index n (wrapping around).")]
        [SerializeField] private Transform[] _spawnPoints;
        [Tooltip("Where the camper starts a match (next to the fireplace). Empty: falls back to the generic spawn points.")]
        [SerializeField] private Transform _camperSpawn;
        [Tooltip("Where gnomes start a match, ideally 12 m or more from the camper. The gnome at position i (gnomes sorted by PlayerId) uses index i. Empty array or entry: falls back to the generic spawn points.")]
        [SerializeField] private Transform[] _gnomeSpawns;
        [Tooltip("When there are more players than spawn points, each extra round is moved this far to the side of the point (m).")]
        [SerializeField, Min(0f)] private float _overflowSpacing = 0.9f;

        private bool _localRequested;

        public static PlayerSpawner Instance { get; private set; }

        public override void Spawned()
        {
            Instance = this;
            SpawnLocalIfMissing();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _localRequested = false;
            if (Instance == this)
                Instance = null;
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (player == Runner.LocalPlayer)
                SpawnLocalIfMissing();
        }

        /// <summary>Where this player starts a match; this object's own pose if no spawn points are set.</summary>
        public (Vector3 position, Quaternion rotation) GetSpawnPose(PlayerRef player)
        {
            if (_spawnPoints is not { Length: > 0 })
                return (transform.position, transform.rotation);

            var index = Mathf.Abs(player.PlayerId);
            var point = _spawnPoints[index % _spawnPoints.Length];
            if (point == null)
                return (transform.position, transform.rotation);

            // Players past the last point reuse the points, shifted sideways so they don't spawn inside each other.
            var round = index / _spawnPoints.Length;
            var side = round % 2 == 0 ? 1f : -1f;
            var offset = point.right * (side * ((round + 1) / 2) * _overflowSpacing);
            return (point.position + offset, point.rotation);
        }

        /// <summary>
        /// Match start pose: camper at <c>_camperSpawn</c>, gnomes at <c>_gnomeSpawns[i]</c> with i = rank among the
        /// gnomes by PlayerId (deterministic on every peer, no networked state). Falls back to <see cref="GetSpawnPose"/>.
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetMatchSpawnPose(PlayerRef player, PlayerRole role)
        {
            var camper = MatchManager.Instance != null ? MatchManager.Instance.Camper : PlayerRef.None;
            var index = -1;
            Transform point = null;
            var pose = (position: Vector3.zero, rotation: Quaternion.identity);

            if (role == PlayerRole.Camper)
            {
                point = _camperSpawn;
                if (point != null)
                    pose = (point.position, point.rotation);
            }
            else if (_gnomeSpawns is { Length: > 0 })
            {
                index = 0;
                foreach (var other in Runner.ActivePlayers)
                    if (other != camper && other.PlayerId < player.PlayerId)
                        index++;

                var round = index / _gnomeSpawns.Length;
                point = _gnomeSpawns[index % _gnomeSpawns.Length];
                if (point != null)
                {
                    // Extra gnomes reuse the points, shifted sideways like GetSpawnPose does.
                    var side = round % 2 == 0 ? 1f : -1f;
                    pose = (point.position + point.right * (side * ((round + 1) / 2) * _overflowSpacing), point.rotation);
                }
            }

            if (point == null)
                pose = GetSpawnPose(player);

            if (Telemetry.Enabled)
            {
                var distance = _camperSpawn != null ? Vector3.Distance(pose.position, _camperSpawn.position) : -1f;
                Telemetry.Begin("spawn").Str("role", role.ToString()).Int("index", index).Float("distance", distance).Send();
            }

            return pose;
        }

        /// <summary>
        /// Puts the local player at its spawn point as <paramref name="role"/>. Respawns only when the role differs;
        /// otherwise just teleports the current character.
        /// </summary>
        public void RespawnLocal(PlayerRole role)
        {
            var (position, rotation) = GetMatchSpawnPose(Runner.LocalPlayer, role);
            var current = PlayerCharacter.Local;
            if (current != null && current.Role == role)
            {
                if (current.TryGetComponent<PlayerMovement>(out var movement))
                    movement.Teleport(position, rotation);
                return;
            }

            Replace(current, role, position, rotation);
        }

        /// <summary>
        /// Replaces the local player's character with the prefab for <paramref name="role"/>, keeping its position.
        /// Must run on the character's owner.
        /// </summary>
        public void ChangeRole(PlayerCharacter current, PlayerRole role)
        {
            var currentTransform = current.transform;
            Replace(current, role, currentTransform.position, currentTransform.rotation);
        }

        private void SpawnLocalIfMissing()
        {
            if (_localRequested || PlayerCharacter.Local != null)
                return;

            _localRequested = true;
            var (position, rotation) = GetSpawnPose(Runner.LocalPlayer);
            Replace(null, PlayerRole.Gnome, position, rotation);
        }

        private void Replace(PlayerCharacter current, PlayerRole role, Vector3 position, Quaternion rotation)
        {
            var prefab = role == PlayerRole.Camper ? _camperPrefab : _gnomePrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[PlayerSpawner] No prefab assigned for role {role}.", this);
                return;
            }

            if (current != null)
            {
                if (current.TryGetComponent<PlayerInteractor>(out var interactor) && interactor.Carried != null)
                    interactor.Carried.RequestRelease(interactor, DropReason.Respawn);

                Runner.Despawn(current.Object);
            }

            Runner.Spawn(prefab, position, rotation, Runner.LocalPlayer);
        }
    }
}
