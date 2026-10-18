using BetterTogether.Audio;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// First-person walk/sprint/jump on a CharacterController. Shared mode: only the state authority
    /// simulates; NetworkTransform replicates the result. All tuning lives on the prefab so it can be reused per role.
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
        private float _verticalVelocity;
        private bool _jumpQueued;

        private static readonly int MovementParameter = Animator.StringToHash("Movement");
        private static readonly int JumpParameter = Animator.StringToHash("IsJump");
        private static readonly int KickParameter = Animator.StringToHash("Kick");

        [Networked] private float AnimationMovement { get; set; }
        [Networked] private NetworkBool AnimationJump { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _networkTransform = GetComponent<NetworkTransform>();
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
        }

        /// <summary>True while the player can't control movement after being knocked back.</summary>
        public bool IsStunned => _stunTimer > 0f;

        /// <summary>
        /// Set by other systems (e.g. the camper's petrifying gaze) to hold the player still: no walking, jumping or
        /// turning the body. Gravity and knockback still apply. The camera can still look around.
        /// </summary>
        public bool IsFrozen { get; set; }

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
        }

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

            if (_networkTransform != null)
                _networkTransform.Teleport(position, rotation);

            if (FirstPersonCamera.Instance != null)
                FirstPersonCamera.Instance.SetYaw(rotation.eulerAngles.y);
        }

        private void Update()
        {
            // FixedUpdateNetwork may not run every rendered frame, so buffer the press.
            // _jump is only assigned on the state authority (see Spawned).
            if (_jump != null && _jump.WasPressedThisFrame())
                _jumpQueued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f; // Small push keeps the controller stuck to the ground.

            var dt = Runner.DeltaTime;
            var stunned = IsStunned || IsFrozen;
            _stunTimer = Mathf.Max(0f, _stunTimer - dt);

            var yaw = FirstPersonCamera.Instance != null && !IsFrozen ? FirstPersonCamera.Instance.Yaw : transform.eulerAngles.y;
            var move = stunned ? Vector2.zero : _move.ReadValue<Vector2>();
            var direction = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y), 1f);
            var speed = _sprint.IsPressed() ? _sprintSpeed : _walkSpeed;

            var jumped = _jumpQueued && grounded && !stunned;
            if (jumped)
                _verticalVelocity = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
            _jumpQueued = false;

            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, (grounded ? _knockbackGroundDrag : _knockbackAirDrag) * dt);

            _verticalVelocity += _gravity * dt;
            _controller.Move((direction * speed + _knockback + Vector3.up * _verticalVelocity) * dt);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // The blend tree uses 0 = idle, 0.5 = walk and 1 = sprint.
            AnimationMovement = Mathf.Clamp01(move.magnitude) * (_sprint.IsPressed() ? 1f : 0.5f);
            AnimationJump = jumped || !_controller.isGrounded;
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

            _animator.SetFloat(MovementParameter, AnimationMovement, 0.1f, Time.deltaTime);
            _animator.SetBool(JumpParameter, AnimationJump);
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
