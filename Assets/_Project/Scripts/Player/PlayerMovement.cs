using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
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
        [Tooltip("Speed multiplier while this player and another gnome carry a heavy item together.")]
        [SerializeField, Range(0.1f, 1f)] private float _pairCarrySpeedMultiplier = 0.7f;
        [Header("Crouch")]
        [Tooltip("Camper: CharacterController height while crouched (m, world units).")]
        [SerializeField, Min(0.1f)] private float _camperCrouchHeight = 1f;
        [Tooltip("Gnome: CharacterController height while crouched (m, world units). Must fit under the 0.3 m gaps.")]
        [SerializeField, Min(0.1f)] private float _gnomeCrouchHeight = 0.22f;
        [Tooltip("Camper: walk speed multiplier while crouched.")]
        [SerializeField, Range(0.1f, 1f)] private float _camperCrouchSpeed = 0.5f;
        [Tooltip("Gnome: walk speed multiplier while crouched.")]
        [SerializeField, Range(0.1f, 1f)] private float _gnomeCrouchSpeed = 0.6f;
        [Tooltip("Camper: eye height above the feet while crouched (m, world units).")]
        [SerializeField, Min(0.05f)] private float _camperCrouchEyeHeight = 0.9f;
        [Tooltip("Gnome: eye height above the feet while crouched (m, world units).")]
        [SerializeField, Min(0.05f)] private float _gnomeCrouchEyeHeight = 0.18f;
        [Tooltip("Layers that block standing up. Camper: Environment + GnomeOnly. Gnome: Environment. Empty = chosen by role in Awake.")]
        [SerializeField] private LayerMask _standUpMask;
        [Header("Animation")]
        [Tooltip("Animator on the visual child (Mesh). Leave empty to find it automatically.")]
        [SerializeField] private Animator _animator;
        [Header("Sound")]
        [Tooltip("Swing of the camper's kick, heard by everyone even when it misses.")]
        [SerializeField] private SoundCue _kickSwingSfx;
        [Header("Heavy carry (items that need two gnomes)")]
        [Tooltip("Pair carry, gnome who turns: degrees per second the gnome orbits the leader while A/D is held.")]
        [SerializeField, Min(1f)] private float _pairTurnSpeed = 90f;
        [Tooltip("Pair carry, gnome who turns: fraction of the item's max gnome span kept as the orbit radius, so the pair stays clear of the drop distance.")]
        [SerializeField, Range(0.3f, 1f)] private float _pairSpanLimit = 0.9f;
        [Tooltip("Pair carry, gnome who turns: top speed (m/s) used to reach the orbit point.")]
        [SerializeField, Min(0.1f)] private float _pairTurnMaxSpeed = 3.5f;
        [Tooltip("Lone gnome holding a heavy item: metres the item's visual is tugged towards the pushed direction (local drawing only).")]
        [SerializeField, Min(0f)] private float _strainPull = 0.08f;
        [Tooltip("Lone gnome holding a heavy item: tremor amplitude in metres while straining.")]
        [SerializeField, Min(0f)] private float _strainShake = 0.015f;
        [Tooltip("Lone gnome holding a heavy item: tremor frequency in Hz.")]
        [SerializeField, Min(0.1f)] private float _strainShakeHz = 14f;
        [Tooltip("Heard by the straining gnome at the item. May be empty (silence).")]
        [SerializeField] private SoundCue _strainSfx;
        [Tooltip("Minimum seconds between two strain sounds.")]
        [SerializeField, Min(0.1f)] private float _strainSfxInterval = 0.7f;
        [Tooltip("Seconds the too-heavy hint stays on screen after the last push.")]
        [SerializeField, Min(0f)] private float _strainHintSeconds = 3f;
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
        private bool _isCaptured;
        private bool _isSpawned;
        private InputAction _crouch;
        private GnomeStack _stack;
        private float _standHeight;
        private float _standRadius;
        private Vector3 _standCenter;
        private bool _appliedCrouch;
        private bool _hasCrouchParameter;
        private float _crouchStartTime;
        private readonly Collider[] _standUpHits = new Collider[8];
        private int _standUpPlayersMask;

        // Heavy carry, owner only. Never networked: the item pose is derived from the replicated positions of the carriers.
        private enum HeavyMode : byte { None, Anchored, Turn }
        private HeavyMode _heavyMode;
        private Vector3 _heavyVelocity;
        private bool _turnReady;
        private float _turnAngle;
        private float _turnSpan;
        private bool _leading;
        private bool _straining;
        private Vector3 _strainDirection;
        private float _strainSeconds;
        private float _lastStrainTime = -100f;
        private Carryable _strainItem;
        private float _strainBlend;
        private float _nextStrainSfx;
        private const float TurnResyncDistance = 0.25f;

        private static readonly int MovementParameter = Animator.StringToHash("Movement");
        private static readonly int JumpParameter = Animator.StringToHash("IsJump");
        private static readonly int StunParameter = Animator.StringToHash("IsStun");
        private static readonly int StatueParameter = Animator.StringToHash("IsStatue");
        private static readonly int DancingParameter = Animator.StringToHash("IsDancing");
        private static readonly int KickParameter = Animator.StringToHash("Kick");
        private static readonly int CrouchParameter = Animator.StringToHash("Crouch");

        [Networked] private float AnimationMovement { get; set; }
        [Networked] private NetworkBool AnimationJump { get; set; }
        [Networked] private NetworkBool AnimationDancing { get; set; }
        // Written only by the owner in FixedUpdateNetwork; everyone reads it through IsCrouching.
        [Networked] private NetworkBool Crouching { get; set; }

        private PlayerInteractor _interactor;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _networkTransform = GetComponent<NetworkTransform>();
            _kickable = GetComponent<Kickable>();
            _petrifiable = GetComponent<Petrifiable>();
            TryGetComponent(out _stack);
            TryGetComponent(out _interactor);
            _standHeight = _controller.height;
            _standRadius = _controller.radius;
            _standCenter = _controller.center;
            if (_standUpMask.value == 0)
                _standUpMask = _petrifiable != null
                    ? LayerMask.GetMask("Environment")
                    : LayerMask.GetMask("Environment", "GnomeOnly");
            _standUpPlayersMask = LayerMask.GetMask("Player", "Gnome");
            if (_animator == null)
                _animator = FindUsableAnimator();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _hasCrouchParameter = HasAnimatorParameter(CrouchParameter);
            ApplyCrouchShape(Crouching);

            if (!HasStateAuthority)
                return;

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move");
            _jump = actions.FindAction("Player/Jump");
            _sprint = actions.FindAction("Player/Sprint");
            _dance = actions.FindAction("Player/Dance");
            _crouch = actions.FindAction("Player/Crouch");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
        }

        /// <summary>True while crouched (replicated, so remote players report it too). False when not spawned.</summary>
        public bool IsCrouching => _isSpawned && Crouching;

        /// <summary>
        /// How far the eye drops below its standing height while crouched, given the standing eye height above the feet.
        /// Both roles go to a serialized absolute eye height (world units).
        /// </summary>
        public float CrouchEyeDrop(float standingEyeHeight)
        {
            var crouchedEye = _petrifiable != null ? _gnomeCrouchEyeHeight : _camperCrouchEyeHeight;
            return Mathf.Max(0f, standingEyeHeight - crouchedEye);
        }

        private float CrouchHeight => _petrifiable != null ? _gnomeCrouchHeight : _camperCrouchHeight;

        private float CrouchSpeed => _petrifiable != null ? _gnomeCrouchSpeed : _camperCrouchSpeed;

        private bool HasAnimatorParameter(int hash)
        {
            if (!CanAnimate)
                return false;

            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Bool)
                    return true;
            }

            return false;
        }

        // Idempotent: resizes the controller from the replicated flag, keeping the feet where they are.
        private void ApplyCrouchShape(bool crouched)
        {
            if (crouched == _appliedCrouch && _isSpawned)
                return;

            _appliedCrouch = crouched;
            var scale = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y));
            var height = crouched ? Mathf.Min(_standHeight, CrouchHeight / scale) : _standHeight;
            _controller.height = height;
            _controller.radius = Mathf.Min(_standRadius, height * 0.5f);
            var center = _standCenter;
            center.y = _standCenter.y - (_standHeight - height) * 0.5f;
            _controller.center = center;
        }

        // Owner only: whether the standing capsule fits at the current spot (environment and other players).
        private bool CanStandUp()
        {
            var lossy = transform.lossyScale;
            var scaleY = Mathf.Max(0.01f, Mathf.Abs(lossy.y));
            var scaleXZ = Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z)));
            var radius = Mathf.Max(0.01f, _standRadius * scaleXZ - 0.02f);
            var feet = transform.position + transform.rotation * Vector3.Scale(_standCenter, lossy) -
                Vector3.up * (_standHeight * 0.5f * scaleY);
            var bottom = feet + Vector3.up * (radius + _controller.skinWidth + 0.01f);
            var top = feet + Vector3.up * (_standHeight * scaleY - radius);
            if (top.y < bottom.y)
                top.y = bottom.y;

            var count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _standUpHits, _standUpMask | _standUpPlayersMask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (_standUpHits[i] != _controller)
                    return false;
            }

            return true;
        }

        // Owner only, in FixedUpdateNetwork: hold Crouch to crouch; on release stand only if there is room.
        private void TickCrouch(bool blocked)
        {
            var crouching = Crouching;
            var hasRiders = _stack != null && _stack.RidersAbove > 0;
            // Blocked (stunned, frozen, match locked) keeps the current state, so a statue doesn't stand up by itself.
            bool wants = blocked ? (bool)crouching : _crouch != null && _crouch.IsPressed();
            if (hasRiders)
                wants = false;
            if (!crouching && wants && (_stack == null || !_stack.IsRiding))
            {
                Crouching = true;
                _crouchStartTime = Time.time;
                if (Telemetry.Enabled) Telemetry.Begin("crouch_start").Send();
            }
            else if (crouching && !wants && CanStandUp())
            {
                Crouching = false;
                if (Telemetry.Enabled) Telemetry.Begin("crouch_end").Float("duration", Time.time - _crouchStartTime).Send();
            }

            ApplyCrouchShape(Crouching);
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

        /// <summary>True while the player is in the air (replicated, so remote players report it too). False when not spawned.</summary>
        public bool IsAirborne => Object != null && Object.IsValid && AnimationJump;

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
        /// Set by <see cref="GnomeCapture"/> on the owner while this gnome is carried by the camper or locked in the cage:
        /// the body is placed by that component, so there is no walking, jumping or physics at all.
        /// </summary>
        public bool IsCaptured
        {
            get => _isCaptured;
            set
            {
                if (value && !_isCaptured)
                {
                    _knockback = Vector3.zero;
                    _verticalVelocity = 0f;
                    _jumpQueued = false;
                    _stunTimer = 0f;
                }

                _isCaptured = value;
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
            if (_jump != null && _jump.WasPressedThisFrame() && !IsRiding && !IsCaptured)
                _jumpQueued = true;
        }

        // Owner only: slowed while carrying an item together with another gnome (replicated holder state, no allocation).
        private float PairCarryMultiplier
        {
            get
            {
                if (_interactor == null)
                    return 1f;
                var held = Carryable.FindHeldBy(_interactor);
                return held != null && held.IsPairCarry ? _pairCarrySpeedMultiplier : 1f;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (IsCaptured)
            {
                CapturedTick();
                return;
            }

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
            var locked = !MatchManager.AllowsMovement || FirstPersonCamera.IsTyping;
            _stunTimer = Mathf.Max(0f, _stunTimer - dt);

            var yaw = FirstPersonCamera.Instance != null && !IsFrozen ? FirstPersonCamera.Instance.Yaw : transform.eulerAngles.y;
            var move = stunned || locked ? Vector2.zero : _move.ReadValue<Vector2>();
            var direction = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y), 1f);
            TickCrouch(stunned || locked);
            TickHeavyCarry(move, yaw, stunned || locked, dt);
            var crouching = Crouching;
            var sprinting = !locked && !crouching && _sprint.IsPressed();
            var speed = (sprinting ? _sprintSpeed : _walkSpeed) * SpeedMultiplier * LoadMultiplier * PairCarryMultiplier * (crouching ? CrouchSpeed : 1f);

            var jumped = _jumpQueued && grounded && !stunned && !locked && !crouching;
            if (jumped)
                _verticalVelocity = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
            _jumpQueued = false;

            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, (grounded ? _knockbackGroundDrag : _knockbackAirDrag) * dt);

            _verticalVelocity += _gravity * dt;
            _controller.Move((PlanarVelocity(direction * speed) + _knockback + Vector3.up * _verticalVelocity) * dt);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // The gnome tree ends at 0.5; the camper tree has walk at 0.5 and sprint at 1.
            var movementScale = _petrifiable != null ? (sprinting ? 0.5f : 0.25f) : (sprinting ? 1f : 0.5f);
            // Quantized to steps of 0.05: Fusion doesn't send a write that leaves the value unchanged, so the
            // quantization is what saves bandwidth (analogue noise no longer changes the value every tick).
            AnimationMovement = Mathf.Round(Mathf.Clamp01(move.magnitude) * movementScale * 20f) / 20f;
            AnimationJump = !stunned && (jumped || !_controller.isGrounded);
            AnimationDancing = _petrifiable != null && _dance != null && _dance.IsPressed() && !stunned && !locked &&
                _controller.isGrounded && move.sqrMagnitude < 0.01f && _knockback.sqrMagnitude < 0.01f;
        }

        /// <summary>Owner only: true for a few seconds after pushing against a heavy item held alone (drives the hint).</summary>
        public bool RecentlyStrained => Time.time - _lastStrainTime < _strainHintSeconds;

        // Owner only: horizontal velocity actually used. Anchored to a heavy item = none; the gnome who turns follows its orbit point.
        private Vector3 PlanarVelocity(Vector3 walking) => _heavyMode switch
        {
            HeavyMode.Anchored => Vector3.zero,
            HeavyMode.Turn => _heavyVelocity,
            _ => walking,
        };

        // Owner only, in FixedUpdateNetwork. Heavy items (two gnomes needed):
        // - alone: anchored, the input only strains (no displacement);
        // - pair: the first holder moves with WASD as usual; the second orbits the first with A/D at a fixed span, reading the
        //   leader's replicated position. Decisions use only replicated state of the other peer; nothing here is networked.
        private void TickHeavyCarry(Vector2 move, float yaw, bool blocked, float dt)
        {
            var previous = _heavyMode;
            _heavyMode = HeavyMode.None;
            _heavyVelocity = Vector3.zero;
            _straining = false;

            var item = _interactor != null ? Carryable.FindHeldBy(_interactor) : null;
            PlayerInteractor leader = null;
            if (item != null && item.IsAnchoringSolo(_interactor))
                _heavyMode = HeavyMode.Anchored;
            else if (item != null && item.TryGetPairLeaderFor(_interactor, out leader))
                _heavyMode = HeavyMode.Turn;

            var leading = item != null && item.IsPairLeader(_interactor);
            if (leading && !_leading && Telemetry.Enabled)
                Telemetry.Begin("pair_carry_start").Str("role", "lead").Str("object", item.DisplayName).Send();
            _leading = leading;

            if (previous != _heavyMode)
                OnHeavyModeChanged(previous, _heavyMode, item);

            if (_heavyMode == HeavyMode.Anchored)
            {
                _strainItem = item;
                if (!blocked && move.sqrMagnitude > 0.04f)
                {
                    _straining = true;
                    _strainDirection = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
                    _strainSeconds += dt;
                    _lastStrainTime = Time.time;
                }
            }
            else if (_heavyMode == HeavyMode.Turn)
            {
                TickTurn(item, leader, move, blocked, dt);
            }
        }

        private void OnHeavyModeChanged(HeavyMode from, HeavyMode to, Carryable item)
        {
            _turnReady = false;
            if (from == HeavyMode.Anchored && _strainSeconds > 0f)
            {
                if (Telemetry.Enabled) Telemetry.Begin("carry_strain").Float("seconds", _strainSeconds).Send();
                _strainSeconds = 0f;
            }

            if (to == HeavyMode.Turn && Telemetry.Enabled)
                Telemetry.Begin("pair_carry_start").Str("role", "turn").Str("object", item.DisplayName).Send();
        }

        // The gnome who turns: A/D moves an angle around the leader; the body walks to that point (never teleports).
        private void TickTurn(Carryable item, PlayerInteractor leader, Vector2 move, bool blocked, float dt)
        {
            // Stunned, frozen or match-locked: stay put, and take the orbit again from where we stand when released.
            if (blocked)
            {
                _turnReady = false;
                return;
            }

            var leaderPosition = leader.transform.position;
            var offset = transform.position - leaderPosition;
            offset.y = 0f;
            var actualAngle = offset.sqrMagnitude > 1e-4f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            if (!_turnReady)
            {
                _turnReady = true;
                _turnSpan = Mathf.Min(offset.magnitude, item.MaxGnomeSpan * _pairSpanLimit);
                _turnAngle = actualAngle;
            }
            else if (Mathf.Abs(Mathf.DeltaAngle(_turnAngle, actualAngle)) * Mathf.Deg2Rad * _turnSpan > TurnResyncDistance)
            {
                // A wall (or the leader's pace) kept us from the orbit point: continue from where we really are.
                _turnAngle = actualAngle;
            }

            _turnAngle += move.x * _pairTurnSpeed * dt;
            var radians = _turnAngle * Mathf.Deg2Rad;
            var target = leaderPosition + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * _turnSpan;
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            _heavyVelocity = dt > 0f ? Vector3.ClampMagnitude(toTarget / dt, _pairTurnMaxSpeed) : Vector3.zero;
        }

        // Render, owner only: the lone holder's item tugs towards the pushed direction with a tremor, and grunts now and then.
        private void PresentHeavyCarry()
        {
            if (!HasStateAuthority)
                return;

            var target = _heavyMode == HeavyMode.Anchored && _straining && !IsRiding && !IsCaptured ? 1f : 0f;
            _strainBlend = Mathf.MoveTowards(_strainBlend, target, Time.deltaTime * 6f);
            if (_strainItem == null)
                return;

            if (_strainBlend <= 0f)
            {
                _strainItem.SetStrainOffset(Vector3.zero);
                _strainItem = null;
                return;
            }

            var phase = Time.time * _strainShakeHz * 2f * Mathf.PI;
            var shake = new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 1.3f) * 0.5f, Mathf.Cos(phase * 0.9f)) * _strainShake;
            _strainItem.SetStrainOffset((_strainDirection * _strainPull + shake) * _strainBlend);

            if (target > 0f && Time.time >= _nextStrainSfx)
            {
                _nextStrainSfx = Time.time + _strainSfxInterval;
                _strainSfx.PlayAt(_strainItem.transform.position);
            }
        }

        // The body is placed by GnomeCapture; only the animation state is cleared.
        private void CapturedTick()
        {
            _jumpQueued = false;
            AnimationMovement = 0f;
            AnimationJump = false;
            AnimationDancing = false;
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
            if (!_isSpawned)
                return;

            ApplyCrouchShape(Crouching);
            PresentHeavyCarry();

            if (!CanAnimate)
                return;

            var isGnome = _petrifiable != null;
            var isStunned = isGnome && _kickable != null && _kickable.IsStunned;
            var isStatue = isGnome && !isStunned && _petrifiable.IsStone;
            var isDancing = isGnome && AnimationDancing && !isStunned && !isStatue;
            _animator.SetFloat(MovementParameter, isStunned || isStatue || isDancing ? 0f : AnimationMovement,
                0.1f, Time.deltaTime);
            _animator.SetBool(JumpParameter, AnimationJump && !isStunned && !isStatue);
            if (_hasCrouchParameter)
                _animator.SetBool(CrouchParameter, Crouching);
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
