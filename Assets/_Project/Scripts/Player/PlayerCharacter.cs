using System.Collections.Generic;
using BetterTogether.Networking;
using Fusion;
using UnityEngine;

namespace BetterTogether.Player
{
    //Claude TODO: add a boolean flag that disables movement until the static event MatchManager.OnGameStarted is called
    /// <summary>
    /// Identity of a spawned player: its role (set on the prefab) and a handle to the local player's character.
    /// </summary>
    public class PlayerCharacter : NetworkBehaviour
    {
        [SerializeField] private PlayerRole _role;

        private static readonly List<PlayerCharacter> _all = new();

        public static PlayerCharacter Local { get; private set; }

        /// <summary>Every spawned player character, local and remote.</summary>
        public static IReadOnlyList<PlayerCharacter> All => _all;

        public PlayerRole Role => _role;

        public override void Spawned()
        {
            name = $"{_role} {Object.StateAuthority.PlayerId}{(HasStateAuthority ? " (local)" : "")}";
            _all.Add(this);

            if (HasStateAuthority)
                Local = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
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

            if (Runner.TryGetComponent<PlayerSpawner>(out var spawner))
                spawner.ChangeRole(this, role);
        }
    }
}
