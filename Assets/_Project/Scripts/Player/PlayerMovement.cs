using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Gaze;
using BetterTogether.Match;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// First-person walk/sprint/jump on a CharacterController. Shared mode: only the state authority
    /// simulates; NetworkTransform replicates the result. All tuning lives on the prefab so it can be reused per role.
    /// Walking, sprinting and jumping are locked while <see cref="MatchManager.AllowsMovement"/> is false.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float _walkSpeed = 2f;
        [SerializeField] private float _sprintSpeed = 3.5f;
        [SerializeField] private float _jumpHeight = 0.5f;
        [SerializeField] private float _gravity = -20f;
        [Header("Animation")]
        [Tooltip("Animator on the visual child (Mesh). Leave empty to find it automatically.")]
        [SerializeField] private Animator _animator;
        [Header("Sound")]
        [Tooltip("Swing of the camper's kick, heard by everyone even when it misses.")]
        [SerializeField] private SoundCue _kickSwingSfx;
        [Header("Knockback")]
        [Tooltip("How fast knockback slows down while airborne (m/s per second).")]
        [SerializeField] private float _knockbackAirDrag = 1.5f;
        [Tooltip("How fast knockback slows down once landed (m/s per second).")]
        [SerializeField] private float _knockbackGroundDrag = 12f;

        private CharacterController _controller;
        private NetworkTransform _networkTransform;
        private Vector3 _knockback;
        private float _stunTimer;
        private InputAction _move;
        private InputAction _jump;
        private InputAction _sprint;
        private InputAction _dance;
        private Kickable _kickable;
        private Petrifiable _petrifiable;
        private float _verticalVelocity;
        private bool _jumpQueued;
        private bool _isRiding;

        private static readonly int MovementParameter = Animator.StringToHash("Movement");
        private static readonly int JumpParameter = Animator.StringToHash("IsJump");
        private static readonly int StunParameter = Animator.StringToHash("IsStun");
        private static readonly int StatueParameter = Animator.StringToHash("IsStatue");
        private static readonly int DancingParameter = Animator.StringToHash("IsDancing");
        private static readonly int KickParameter = Animator.StringToHash("Kick");

        [Networked] private float AnimationMovement { get; set; }
        [Networked] private NetworkBool AnimationJump { get; set; }
        [Networked] private NetworkBool AnimationDancing { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _networkTransform = GetComponent<NetworkTransform>();
            _kickable = GetComponent<Kickable>();
            _petrifiable = GetComponent<Petrifiable>();
            if (_animator == null)
                _animator = FindUsableAnimator();
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move");
            _jump = actions.FindAction("Player/Jump");
            _sprint = actions.FindAction("Player/Sprint");
            _dance = actions.FindAction("Player/Dance");
        }

        /// <summary>True while the player can't control movement after being knocked back.</summary>
        public bool IsStunned => _stunTimer > 0f;

        /// <summary>
        /// Set by other systems (e.g. the camper's petrifying gaze) to hold the player still: no walking, jumping or
        /// turning the body. Gravity and knockback still apply. The camera can still look around.
        /// </summary>
        public bool IsFrozen { get; set; }

        /// <summary>Multiplies walk and sprint speed, e.g. lowered while the camper focuses its gaze.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Multiplies walk and sprint speed while carrying other gnomes on the head (see GnomeStack).</summary>
        public float LoadMultiplier { get; set; } = 1f;

        /// <summary>
        /// Set by <see cref="GnomeStack"/> on the owner while this gnome sits on another one: no walking, jumping or
        /// physics, only turning the body. Pending knockback is kept for when the gnome gets off.
        /// </summary>
        public bool IsRiding
        {
            get => _isRiding;
            set
            {
                if (value && !_isRiding)
                {
                    _knockback = Vector3.zero;
                    _verticalVelocity = 0f;
                    _jumpQueued = false;
                }

                _isRiding = value;
            }
        }

        /// <summary>
        /// Launches the player. Only has effect on the state authority, which is the one simulating movement.
        /// </summary>
        public void ApplyKnockback(Vector3 velocity, float stunDuration)
        {
            if (!HasStateAuthority)
                return;

            _knockback = new Vector3(velocity.x, 0f, velocity.z);
            _verticalVelocity = velocity.y;
            _stunTimer = Mathf.Max(_stunTimer, stunDuration);
            _jumpQueued = false;
            AnimationMovement = 0f;
            AnimationJump = false;
            AnimationDancing = false;
        }

        /// <summary>Raised on the owner at the end of <see cref="Teleport"/>, so systems tied to the old position (e.g. riding another gnome) can let go.</summary>
        public event System.Action Teleported;

        /// <summary>Moves the player instantly (e.g. back to its spawn point). State authority only.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!HasStateAuthority)
                return;

            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;

            _knockback = Vector3.zero;
            _verticalVelocity = 0f;
            _stunTimer = 0f;
            _jumpQueued = false;
            AnimationMovement = 0f;
            AnimationJump = false;
            AnimationDancing = false;

            if (_networkTransform != null)
                _networkTransform.Teleport(position, rotation);

            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.SetYaw(rotation.eulerAngles.y);

            Teleported?.Invoke();
        }

        private void Update()
        {
            // FixedUpdateNetwork may not run every rendered frame, so buffer the press.
            // _jump is only assigned on the state authority (see Spawned).
            if (_jump != null && _jump.WasPressedThisFrame() && !IsRiding)
                _jumpQueued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (IsRiding)
            {
                RideTick();
                return;
            }

            var grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f; // Small push keeps the controller stuck to the ground.

            var dt = Runner.DeltaTime;
            var stunned = IsStunned || IsFrozen;
            var locked = !MatchManager.AllowsMovement;
            _stunTimer = Mathf.Max(0f, _stunTimer - dt);

            var yaw = FirstPersonCamera.Instance != null && !IsFrozen ? FirstPersonCamera.Instance.Yaw : transform.eulerAngles.y;
            var move = stunned || locked ? Vector2.zero : _move.ReadValue<Vector2>();
            var direction = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y), 1f);
            var sprinting = !locked && _sprint.IsPressed();
            var speed = (sprinting ? _sprintSpeed : _walkSpeed) * SpeedMultiplier * LoadMultiplier;

            var jumped = _jumpQueued && grounded && !stunned && !locked;
            if (jumped)
                _verticalVelocity = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
            _jumpQueued = false;

            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, (grounded ? _knockbackGroundDrag : _knockbackAirDrag) * dt);

            _verticalVelocity += _gravity * dt;
            _controller.Move((direction * speed + _knockback + Vector3.up * _verticalVelocity) * dt);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // The gnome tree ends at 0.5; the camper tree has walk at 0.5 and sprint at 1.
            var movementScale = _petrifiable != null ? (sprinting ? 0.5f : 0.25f) : (sprinting ? 1f : 0.5f);
            AnimationMovement = Mathf.Clamp01(move.magnitude) * movementScale;
            AnimationJump = !stunned && (jumped || !_controller.isGrounded);
            AnimationDancing = _petrifiable != null && _dance != null && _dance.IsPressed() && !stunned && !locked &&
                _controller.isGrounded && move.sqrMagnitude < 0.01f && _knockback.sqrMagnitude < 0.01f;
        }

        // The controller is disabled while riding and the mount carries the body; only the turning and the timers go on.
        private void RideTick()
        {
            _stunTimer = Mathf.Max(0f, _stunTimer - Runner.DeltaTime);
            _jumpQueued = false;

            var yaw = FirstPersonCamera.Instance != null && !IsFrozen ? FirstPersonCamera.Instance.Yaw : transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            AnimationMovement = 0f;
            AnimationJump = false;
            AnimationDancing = false;
        }

        // Models can carry an Animator without a controller (or an inactive alternative mesh); animate only a usable one.
        private Animator FindUsableAnimator()
        {
            foreach (var animator in GetComponentsInChildren<Animator>())
            {
                if (animator.runtimeAnimatorController != null)
                    return animator;
            }

            return null;
        }

        private bool CanAnimate => _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

        public override void Render()
        {
            if (!CanAnimate)
                return;

            var isGnome = _petrifiable != null;
            var isStunned = isGnome && _kickable != null && _kickable.IsStunned;
            var isStatue = isGnome && !isStunned && _petrifiable.IsStone;
            var isDancing = isGnome && AnimationDancing && !isStunned && !isStatue;
            _animator.SetFloat(MovementParameter, isStunned || isStatue || isDancing ? 0f : AnimationMovement,
                0.1f, Time.deltaTime);
            _animator.SetBool(JumpParameter, AnimationJump && !isStunned && !isStatue);
            if (isGnome)
            {
                _animator.SetBool(StunParameter, isStunned);
                _animator.SetBool(StatueParameter, isStatue);
                _animator.SetBool(DancingParameter, isDancing);
            }
        }

        /// <summary>Plays the camper's attack on every client, including when the kick misses.</summary>
        public void PlayKickAnimation()
        {
            if (HasStateAuthority)
                RPC_PlayKickAnimation();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayKickAnimation()
        {
            _kickSwingSfx.PlayAt(transform.position);
            if (!CanAnimate)
                return;

            _animator.ResetTrigger(KickParameter);
            _animator.SetTrigger(KickParameter);
        }
    }
}
