using BetterTogether.Audio;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// A player that freezes while any camper is looking at it (see <see cref="CamperGaze"/>).
    /// Runs on the owner, the only client that moves this player.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class Petrifiable : NetworkBehaviour
    {
        [Tooltip("Heights above the feet that are checked; seeing any of them counts (e.g. feet and head).")]
        [SerializeField] private float[] _checkHeights = { 0.1f, 0.4f };
        [Tooltip("Tension sting when the camper starts looking at you. Only you hear it; set the cue to not Spatial.")]
        [SerializeField] private SoundCue _watchedSfx;

        private PlayerMovement _movement;

        public static Petrifiable Local { get; private set; }

        /// <summary>Only meaningful on the owner.</summary>
        public bool IsWatched { get; private set; }

        private void Awake() => _movement = GetComponent<PlayerMovement>();

        public override void Spawned()
        {
            if (HasStateAuthority)
                Local = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (Local != this)
                return;

            var watched = IsSeenByAnyCamper();
            if (watched && !IsWatched)
                _watchedSfx.Play(); // Only this gnome hears it (2D), like the HUD warning.

            IsWatched = watched;
            _movement.IsFrozen = IsWatched;
        }

        private bool IsSeenByAnyCamper()
        {
            var feet = transform.position;
            foreach (var gaze in CamperGaze.All)
            {
                if (gaze == null || gaze.Object == null || !gaze.Object.IsValid)
                    continue;

                foreach (var height in _checkHeights)
                {
                    if (gaze.CanSee(feet + Vector3.up * height, transform))
                        return true;
                }
            }

            return false;
        }
    }
}
