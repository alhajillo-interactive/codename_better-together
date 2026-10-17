using BetterTogether.Interaction;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Combat
{
    /// <summary>
    /// Lets a player be kicked. Shared mode: the kicker doesn't own this player, so the kick travels as an RPC
    /// to the owner, who launches its own movement and drops whatever it was carrying.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class Kickable : NetworkBehaviour
    {
        [Tooltip("Upper bound for the kick speed, so a modified client can't send players to orbit.")]
        [SerializeField] private float _maxKickSpeed = 15f;
        [Tooltip("Seconds without movement control after being kicked.")]
        [SerializeField] private float _stunDuration = 0.6f;
        [Tooltip("Where the dropped item lands between this player (0) and the kicker (1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _dropTowardKicker = 0.5f;

        private PlayerMovement _movement;
        private PlayerInteractor _interactor;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _interactor = GetComponent<PlayerInteractor>();
        }

        /// <summary>Called by the kicker's client on its local copy of this player.</summary>
        public void Kick(Vector3 velocity, Vector3 kickerPosition) => RPC_Kick(velocity, kickerPosition);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Kick(Vector3 velocity, Vector3 kickerPosition)
        {
            DropCarried(kickerPosition);
            _movement.ApplyKnockback(Vector3.ClampMagnitude(velocity, _maxKickSpeed), _stunDuration);
        }

        private void DropCarried(Vector3 kickerPosition)
        {
            var carried = _interactor != null ? _interactor.Carried : null;
            if (carried == null)
                return;

            var position = transform.position;
            var toKicker = Vector3.ProjectOnPlane(kickerPosition - position, Vector3.up);
            carried.DropAt(position + toKicker * _dropTowardKicker);
        }
    }
}
