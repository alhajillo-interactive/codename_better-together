using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Plays a footstep every time the character has walked a stride's distance on the ground. Runs on every client
    /// (local and remote players alike) from how far the transform actually moved, so it needs no networking.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class Footsteps : MonoBehaviour
    {
        [Tooltip("Played at the character's feet. Usually a cue with several variations.")]
        [SerializeField] private SoundCue _stepSfx;
        [Tooltip("Metres walked between two steps.")]
        [SerializeField, Min(0.1f)] private float _strideLength = 0.5f;
        [Tooltip("Below this horizontal speed (m/s) nothing is heard, so drifting or settling stays quiet.")]
        [SerializeField, Min(0f)] private float _minSpeed = 0.5f;
        [Tooltip("A single-frame move longer than this (m) is a teleport and does not count.")]
        [SerializeField, Min(0f)] private float _teleportDistance = 2f;

        private PlayerMovement _movement;
        private GnomeStack _stack;
        private Vector3 _lastPosition;
        private float _distance;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            TryGetComponent(out _stack);
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _distance = 0f;
        }

        private void Update()
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;

            var moved = delta.magnitude;
            if (moved > _teleportDistance || !CanStep())
            {
                // Teleporting, in the air or riding: the next step starts a fresh stride.
                _distance = 0f;
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f || moved / dt < _minSpeed)
                return;

            _distance += moved;
            if (_distance < _strideLength)
                return;

            _distance = 0f;
            _stepSfx.PlayAt(position);
        }

        private bool CanStep() => !_movement.IsAirborne && (_stack == null || !_stack.IsRiding);
    }
}
