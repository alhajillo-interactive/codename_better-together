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

        public static PlayerCharacter Local { get; private set; }

        public PlayerRole Role => _role;

        public override void Spawned()
        {
            name = $"{_role} {Object.StateAuthority.PlayerId}{(HasStateAuthority ? " (local)" : "")}";

            if (HasStateAuthority)
                Local = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }
    }
}
