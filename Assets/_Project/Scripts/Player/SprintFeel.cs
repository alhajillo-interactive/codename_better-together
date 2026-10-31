using BetterTogether.Gaze;
using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// Presentation of running. Every peer derives "is sprinting on the ground" from the real horizontal speed between frames
    /// (nothing is networked, no RPC) and puffs dust and leaves at the feet every <see cref="_emitInterval"/> seconds, scaled by role.
    /// The owner also exposes <see cref="SpeedFxAmount"/> (0..1, 200 ms fade) that <c>SpeedLinesHUD</c> turns into wind lines.
    /// Nothing is drawn in the water, in the air, riding a stack, or while the camper has the camera raised.
    /// A late joiner or a master change only starts measuring from the current position; a player who leaves takes it along.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class SprintFeel : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Real horizontal speed (m/s) from which the player counts as sprinting (walk is 2, sprint 3.5).")]
        [SerializeField, Min(0.1f)] private float _sprintSpeed = 2.8f;
        [Tooltip("Seconds over which the measured speed is smoothed (frames are not tick-aligned on proxies).")]
        [SerializeField, Min(0.01f)] private float _speedSmoothing = 0.08f;
        [Tooltip("A single-frame move longer than this (m) is a teleport and is ignored.")]
        [SerializeField, Min(0f)] private float _teleportDistance = 2f;
        [Tooltip("Seconds the wind-line amount takes to fade in and out.")]
        [SerializeField, Min(0.01f)] private float _fadeTime = 0.2f;

        [Header("Feet particles (every peer)")]
        [Tooltip("Dust puffs at the feet. Manual Emit only (world simulation space), child of this player at the feet.")]
        [SerializeField] private ParticleSystem _dust;
        [Tooltip("Leaves kicked up at the feet. Manual Emit only (world simulation space).")]
        [SerializeField] private ParticleSystem _leaves;
        [Tooltip("Seconds between two puffs while sprinting.")]
        [SerializeField, Min(0.02f)] private float _emitInterval = 0.12f;
        [Tooltip("Dust particles per puff.")]
        [SerializeField, Min(0)] private int _dustCount = 2;
        [Tooltip("One puff in this many also kicks up a leaf (1 = every puff).")]
        [SerializeField, Min(1)] private int _leafEvery = 3;
        [Tooltip("Dust particle size (m) for the camper; the gnome uses the role scale below.")]
        [SerializeField, Min(0.001f)] private float _dustSize = 0.12f;
        [Tooltip("Leaf particle size (m) for the camper.")]
        [SerializeField, Min(0.001f)] private float _leafSize = 0.06f;
        [Tooltip("Size multiplier for a gnome (they are small).")]
        [SerializeField, Min(0.05f)] private float _gnomeScale = 0.35f;

        private PlayerMovement _movement;
        private GnomeStack _stack;
        private PlayerCharacter _character;
        private bool _isGnome;
        private Vector3 _lastPosition;
        private bool _hasLast;
        private float _speed;
        private float _nextEmit;
        private int _puffCount;
        private float _amount;
        private ParticleSystem.EmitParams _emit;

        /// <summary>The local player's sprint feel (owner only).</summary>
        public static SprintFeel Local { get; private set; }

        /// <summary>Owner only: 0..1 amount of the speed effects (0 when not sprinting on the ground or when the camera is raised).</summary>
        public float SpeedFxAmount => _amount;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            TryGetComponent(out _character);
            TryGetComponent(out _stack);
            _isGnome = TryGetComponent<Petrifiable>(out _);
        }

        private void OnEnable()
        {
            _hasLast = false;
            _speed = 0f;
            _amount = 0f;
            _puffCount = 0;
        }

        private void OnDisable()
        {
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (!_movement.IsSpawned)
            {
                _hasLast = false;
                _amount = 0f;
                return;
            }

            var isOwner = _character != null && PlayerCharacter.Local == _character;
            if (isOwner)
                Local = this;
            else if (Local == this)
                Local = null;

            var dt = Time.deltaTime;
            var position = transform.position;
            if (!_hasLast || dt <= 0f)
            {
                // First look (late joiner, new spawn, master change): start measuring from here, no effect yet.
                _hasLast = true;
                _lastPosition = position;
                return;
            }

            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;
            var moved = delta.magnitude;
            if (moved > _teleportDistance)
            {
                _speed = 0f;
                return;
            }

            _speed = Mathf.Lerp(_speed, moved / dt, 1f - Mathf.Exp(-dt / _speedSmoothing));

            var onGround = !_movement.IsAirborne && !_movement.InWater && (_stack == null || !_stack.IsRiding);
            var sprinting = onGround && _speed >= _sprintSpeed;

            if (isOwner)
            {
                var gaze = CamperGaze.Local;
                var raised = gaze != null && gaze.Focusing;
                var target = sprinting && !raised ? 1f : 0f;
                _amount = Mathf.MoveTowards(_amount, target, dt / _fadeTime);
            }

            if (sprinting && Time.time >= _nextEmit)
                EmitFeet(position);
        }

        private void EmitFeet(Vector3 feet)
        {
            _nextEmit = Time.time + _emitInterval;
            var scale = _isGnome ? _gnomeScale : 1f;
            _emit.position = feet;
            if (_dust != null && _dustCount > 0)
            {
                _emit.startSize = _dustSize * scale;
                _dust.Emit(_emit, _dustCount);
            }

            _puffCount++;
            if (_leaves != null && _puffCount % _leafEvery == 0)
            {
                _emit.startSize = _leafSize * scale;
                _leaves.Emit(_emit, 1);
            }
        }
    }
}
