using BetterTogether.Gaze;
using BetterTogether.Match;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A rabbit hole: a gnome with empty hands enters and comes out of the next one. Objects never go through, so nothing can end up
    /// where the camper can't reach. Scene object without networking: the owner moves its own character and NetworkTransform
    /// replicates it, so late joiners, master changes and leaving players need nothing here.
    /// </summary>
    public class HuecoConejo : MonoBehaviour, IInteractable
    {
        [Tooltip("Where this hole leads.")]
        [SerializeField] private HuecoConejo _next;
        [Tooltip("Metres from the hole where a gnome comes out.")]
        [SerializeField, Min(0f)] private float _exitDistance = 0.9f;
        [Tooltip("Seconds before the same gnome can use a hole again.")]
        [SerializeField, Min(0f)] private float _cooldown = 3f;

        private static float _readyAt;

        public string GetPrompt(PlayerInteractor interactor) => "Saltar al siguiente hueco";

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || _next == null || !MatchManager.AllowsInteraction || interactor.Role != PlayerRole.Gnome)
                return false;

            // A tower stays out: its rider could carry an object through.
            if (Time.time < _readyAt || interactor.CarriedOrPredicted != null || (interactor.TryGetComponent<GnomeStack>(out var stack) && stack.Rider != null))
                return false;

            return !interactor.TryGetComponent<Petrifiable>(out var stone) || (!stone.IsStone && !stone.IsLockedInCage);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) || !interactor.TryGetComponent<PlayerMovement>(out var movement))
                return;

            _readyAt = Time.time + _cooldown;
            var exit = _next.transform;
            movement.Teleport(exit.position + exit.forward * _exitDistance + Vector3.up * 0.1f, exit.rotation);
            if (interactor.TryGetComponent<GnomeStatues>(out var statues))
                statues.RPC_Hop(transform.position, exit.position);
        }
    }
}
