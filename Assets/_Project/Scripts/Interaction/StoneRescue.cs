using BetterTogether.Gaze;
using BetterTogether.Match;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A teammate breaks a stone gnome free by holding the interact key on it. Local aim target on the gnome; the gnome's owner
    /// validates through <see cref="Petrifiable.RPC_RescueStone"/> and applies the same release as breaking out alone.
    /// </summary>
    public class StoneRescue : MonoBehaviour, IInteractable, IHoldInteractable
    {
        [Tooltip("Seconds the teammate holds the key.")]
        [SerializeField, Min(0f)] private float _holdSeconds = 1f;

        private Petrifiable _gnome;

        private void Awake() => _gnome = GetComponentInParent<Petrifiable>();

        public string GetPrompt(PlayerInteractor interactor) => "Mantén para romper la piedra";

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!MatchManager.AllowsInteraction || _gnome == null || !_gnome.IsStone || _gnome.IsLockedInCage || interactor.Role != PlayerRole.Gnome)
                return false;

            return !interactor.TryGetComponent<Petrifiable>(out var own) || !own.IsHeldStone;
        }

        public void Interact(PlayerInteractor interactor)
        {
        }

        public float GetHoldTime(PlayerInteractor interactor) => _holdSeconds;

        public void OnHoldComplete(PlayerInteractor interactor) => _gnome.RPC_RescueStone();
    }
}
