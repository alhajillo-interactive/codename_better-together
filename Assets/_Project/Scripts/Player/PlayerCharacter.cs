using System.Collections.Generic;
using BetterTogether.Networking;
using Fusion;
using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// Identity of a spawned player: its role (set on the prefab) and a handle to the local player's character.
    /// </summary>
    public class PlayerCharacter : NetworkBehaviour
    {
        [SerializeField] private PlayerRole _role;

        private static readonly List<PlayerCharacter> _all = new();

        private bool _isSpawned;

        public static PlayerCharacter Local { get; private set; }

        /// <summary>Every spawned player character, local and remote.</summary>
        public static IReadOnlyList<PlayerCharacter> All => _all;

        public PlayerRole Role => _role;

        [Networked] private NetworkString<_16> NetName { get; set; }

        /// <summary>Name chosen by this player, or "Jugador N" when none is known. Safe to read at any time.</summary>
        public string DisplayName
        {
            get
            {
                if (!_isSpawned)
                    return "Jugador";

                var playerName = LocalPlayerProfile.Sanitize(NetName.ToString());
                return string.IsNullOrEmpty(playerName) ? $"Jugador {Object.StateAuthority.PlayerId}" : playerName;
            }
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);

            if (HasStateAuthority)
            {
                NetName = LocalPlayerProfile.DisplayNameOrDefault(Object.StateAuthority.PlayerId);
                Local = this;
            }

            name = $"{_role} {Object.StateAuthority.PlayerId} {DisplayName}{(HasStateAuthority ? " (local)" : "")}";
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _all.Remove(this);

            if (Local == this)
                Local = null;
        }

        /// <summary>
        /// Asks this player's owner to respawn as <paramref name="role"/>. Anyone may call it (debug role panel);
        /// the owner does the respawn because in Shared mode only it controls its own character.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestRole(PlayerRole role)
        {
            if (role == _role)
                return;

            var spawner = PlayerSpawner.Instance;
            if (spawner != null)
                spawner.ChangeRole(this, role);
        }
    }
}
