using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Interaction;
using BetterTogether.Match;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Lets gnomes climb on top of each other to form a tower. Aim at a gnome and press Attack to climb on its tower,
    /// press Jump to hop off. The rider is carried by whoever is below it and the base walks slower for every gnome it
    /// carries. Kicking the base topples everyone above it.
    /// Shared mode: every gnome owns its networked <see cref="Mount"/> (the gnome right below it) and only writes that;
    /// every client then poses riders on their mounts in LateUpdate, before the camera follows them.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(-300)]
    public class GnomeStack : NetworkBehaviour
    {
        [Header("Tower")]
        [Tooltip("Most gnomes in a tower, counting the base.")]
        [SerializeField, Min(1)] private int _maxTowerHeight = 3;
        [Tooltip("Metres above a gnome's root where its rider's root sits.")]
        [SerializeField, Min(0f)] private float _seatHeight = 0.62f;
        [Tooltip("Walk and sprint speed multiplier of a gnome for every gnome it carries above it.")]
        [SerializeField, Range(0.1f, 1f)] private float _speedPerRider = 0.85f;

        [Header("Climbing")]
        [Tooltip("How far from the camera a gnome can be to climb on it.")]
        [SerializeField, Min(0f)] private float _reach = 1.6f;
        [Tooltip("Largest distance between the camera ray and a gnome's body for it to count as aimed.")]
        [SerializeField, Min(0f)] private float _aimRadius = 0.3f;
        [Tooltip("Height of a gnome's body above its root; the aim checks the line from the root to this height.")]
        [SerializeField, Min(0f)] private float _bodyHeight = 0.6f;
        [Tooltip("Layers that can block the line of sight to the aimed gnome.")]
        [SerializeField] private LayerMask _aimMask = ~0;
        [Tooltip("Seconds after getting off (or being dropped) before the gnome can climb again.")]
        [SerializeField, Min(0f)] private float _remountCooldown = 0.4f;
        [Tooltip("Seconds the climb takes to ease from where the gnome stood up to the seat.")]
        [SerializeField, Min(0f)] private float _climbTime = 0.12f;

        [Header("Dismount")]
        [Tooltip("Forward speed of the hop when getting off.")]
        [SerializeField] private float _dismountForward = 1.5f;
        [Tooltip("Upward speed of the hop when getting off.")]
        [SerializeField] private float _dismountUp = 3f;
        [Tooltip("Seconds without control after the hop.")]
        [SerializeField, Min(0f)] private float _dismountStun = 0.1f;

        [Header("Passing items")]
        [Tooltip("Seconds after passing an item before this gnome can pass another one.")]
        [SerializeField, Min(0f)] private float _handOffCooldown = 0.35f;

        [Header("Feedback")]
        [Tooltip("Field of view added for a moment on the local camera when climbing.")]
        [SerializeField] private float _mountFovPunch = 3f;
        [Tooltip("Seconds the field of view takes to ease back after climbing.")]
        [SerializeField] private float _mountFovPunchTime = 0.2f;
        [Tooltip("Heard by everyone, from where the gnome climbs.")]
        [SerializeField] private SoundCue _mountSfx;
        [Tooltip("Heard by everyone, from where the gnome gets off.")]
        [SerializeField] private SoundCue _dismountSfx;

        private const float MinSightHeight = 0.15f;
        private const float SightMargin = 0.02f;

        private static readonly List<GnomeStack> _all = new();

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private PlayerMovement _movement;
        private PlayerCharacter _character;
        private PlayerInteractor _interactor;
        private Kickable _kickable;
        private CharacterController _controller;
        private InputAction _attack;
        private InputAction _jump;
        private InputAction _focus;
        private float _nextHandOffTime;
        private GnomeStack _aimed;
        private float _nextClimbTime;
        private bool _dismountRequested;
        private ChangeDetector _changes;
        private Vector3 _lastFreePosition;
        private Vector3 _climbFrom;
        private float _climbStartTime;
        private bool _climbing;
        private bool _mountHadMount;
        private bool _isSpawned;

        [Networked] private GnomeStack MountRef { get; set; }

        /// <summary>The local player's stack component, if it is a gnome.</summary>
        public static GnomeStack Local { get; private set; }

        /// <summary>The gnome this one is standing on, or null when on the ground. Safe to read at any time.</summary>
        public GnomeStack Mount
        {
            get
            {
                if (!_isSpawned)
                    return null;

                var mount = MountRef;
                return mount != null && mount != this && mount._isSpawned && mount.Object != null && mount.Object.IsValid ? mount : null;
            }
        }

        public bool IsRiding => Mount != null;

        /// <summary>The interactor that carries things for this gnome.</summary>
        public PlayerInteractor Interactor => _interactor;

        /// <summary>The gnome standing directly on this one; if several claim it, the one with the lowest player id.</summary>
        public GnomeStack Rider
        {
            get
            {
                GnomeStack best = null;
                foreach (var other in _all)
                {
                    if (other != this && other.Mount == this && (best == null || other.OwnerId < best.OwnerId))
                        best = other;
                }

                return best;
            }
        }

        /// <summary>The gnome at the bottom of this tower (this one when on the ground).</summary>
        public GnomeStack Base
        {
            get
            {
                var current = this;
                for (var i = 0; i <= _maxTowerHeight; i++)
                {
                    var mount = current.Mount;
                    if (mount == null)
                        break;

                    current = mount;
                }

                return current;
            }
        }

        /// <summary>The gnome at the top of this tower (this one when nobody is on it).</summary>
        public GnomeStack Top
        {
            get
            {
                var current = this;
                for (var i = 0; i <= _maxTowerHeight; i++)
                {
                    var rider = current.Rider;
                    if (rider == null)
                        break;

                    current = rider;
                }

                return current;
            }
        }

        /// <summary>Gnomes in this tower, from its base to its top.</summary>
        public int TowerHeight => Base.RidersAbove + 1;

        /// <summary>Gnomes on top of this one, up to the top of the tower.</summary>
        public int RidersAbove
        {
            get
            {
                var count = 0;
                var current = this;
                for (var i = 0; i <= _maxTowerHeight; i++)
                {
                    current = current.Rider;
                    if (current == null)
                        break;

                    count++;
                }

                return count;
            }
        }

        /// <summary>Where this gnome's root is posed: on its mount's seat when riding, otherwise where it really is.</summary>
        public Vector3 PosedPosition => Posed(0);

        /// <summary>Metres above this gnome's root where its rider's root sits.</summary>
        public float SeatHeight => _seatHeight;

        /// <summary>Where this gnome's rider sits.</summary>
        public Vector3 SeatPosition => PosedPosition + Vector3.up * _seatHeight;

        /// <summary>Owner only: the gnome under the crosshair within reach, or null.</summary>
        public GnomeStack Aimed => Local == this && _aimed != null && _aimed._isSpawned ? _aimed : null;

        /// <summary>Owner only: whether pressing Attack now would climb on <see cref="Aimed"/>.</summary>
        public bool CanClimbAimed => EvaluateClimb(Aimed, out _);

        /// <summary>Owner only: why <see cref="Aimed"/> can't be climbed right now, for the HUD; null when it can or nothing is aimed.</summary>
        public string ClimbBlockReason
        {
            get
            {
                EvaluateClimb(Aimed, out var reason);
                return reason;
            }
        }

        private int OwnerId => _isSpawned && Object != null && Object.IsValid ? Object.StateAuthority.PlayerId : int.MaxValue;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _character = GetComponent<PlayerCharacter>();
            _interactor = GetComponent<PlayerInteractor>();
            _kickable = GetComponent<Kickable>();
            _controller = GetComponent<CharacterController>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _lastFreePosition = transform.position;

            if (!HasStateAuthority)
                return;

            Local = this;
            _movement.Teleported += HandleTeleported;
            _attack = InputSystem.actions.FindAction("Player/Attack");
            _jump = InputSystem.actions.FindAction("Player/Jump");
            _focus = InputSystem.actions.FindAction("Player/Focus");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _all.Remove(this);
            _movement.Teleported -= HandleTeleported;
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (!_isSpawned || Local != this)
                return;

            UpdateAim();

            // The hop itself runs in LateUpdate, after PlayerMovement.Update has seen (and ignored) this same press.
            if (IsRiding && _jump != null && _jump.WasPressedThisFrame() && !_movement.IsFrozen)
                _dismountRequested = true;

            // Holding something while next to another gnome in a tower: the clicks pass it on (climbing needs empty hands).
            if (TryHandOff() || IsRiding)
                return;

            // Left click also re-locks the cursor (FirstPersonCamera), so ignore it while the cursor is free.
            if (_attack != null && _attack.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked)
                TryClimb();
        }

        // Attack passes the item to the rider above, Focus (right click) to the mount below.
        private bool TryHandOff()
        {
            if (_interactor == null || Time.time < _nextHandOffTime || Cursor.lockState != CursorLockMode.Locked)
                return false;

            var carried = _interactor.Carried;
            if (carried == null)
                return false;

            GnomeStack target;
            if (_attack != null && _attack.WasPressedThisFrame())
                target = Rider;
            else if (_focus != null && _focus.WasPressedThisFrame())
                target = Mount;
            else
                return false;

            if (target == null || target._interactor == null)
                return false;

            if (carried.CanHandOff(_interactor, target._interactor, out _))
            {
                carried.RequestHandOff(_interactor, target._interactor);
                _nextHandOffTime = Time.time + _handOffCooldown;
            }

            return true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            _movement.LoadMultiplier = Mathf.Pow(_speedPerRider, RidersAbove);

            if (_movement.IsRiding || MountRef != null)
                TickRiding();
        }

        // Owner only: ends the ride when something invalidates it, otherwise keeps the body on the seat.
        private void TickRiding()
        {
            var mount = Mount;
            if (mount == null || !MatchManager.AllowsInteraction || _movement.IsStunned)
            {
                // A stun here usually is a knockback already applied (e.g. Petrifiable's release hop), which flies the gnome off.
                if (mount != null && _movement.IsStunned)
                    DropCarried();
                ClearMount();
                return;
            }

            // The gnome below got off or fell, so the whole stack above it falls too.
            var mountRides = mount.Mount != null && !ChainContains(mount, this);
            var mountLeft = _mountHadMount && !mountRides;
            _mountHadMount = mountRides;
            if (mountLeft || TowerToppled(mount))
            {
                DropCarried();
                ClearMount();
                _movement.ApplyKnockback(TopplePushDirection(mount) * _dismountForward + Vector3.up * _dismountUp, _dismountStun);
                return;
            }

            if (LosesRace(mount) || LosesCycle() || (Rider == null && TowerHeight > _maxTowerHeight))
            {
                ClearMount();
                return;
            }

            // Replicate roughly the right spot, so the camper's gaze and raycasts see the rider where it sits.
            transform.position = mount.SeatPosition;
        }

        // Falling off with something in the hands: it stays where the gnome was.
        private void DropCarried()
        {
            var carried = _interactor != null ? _interactor.Carried : null;
            if (carried != null)
                carried.RequestRelease(_interactor, transform.position, DropReason.Fall);
        }

        private void HandleTeleported()
        {
            // The teleport already moved the body and enabled the controller; just let go of the mount.
            if (_movement.IsRiding || MountRef != null)
                ClearMount();
        }

        private void LateUpdate()
        {
            // LateUpdate also runs before Spawned / after Despawned, when networked state can't be read.
            if (!_isSpawned)
                return;

            if (_dismountRequested)
            {
                _dismountRequested = false;
                if (Local == this && IsRiding)
                    Dismount();
            }

            var mount = Mount;
            SetRidingBody(mount != null);
            if (mount == null)
            {
                _climbing = false;
                _lastFreePosition = transform.position;
                return;
            }

            transform.position = ClimbBlend(mount.SeatPosition);
        }

        public override void Render()
        {
            // The mount is networked, so this runs on every client: everyone hears the climb and the hop.
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change != nameof(MountRef))
                    continue;

                if (Mount == null)
                {
                    _dismountSfx.PlayAt(transform.position);
                    continue;
                }

                _mountSfx.PlayAt(transform.position);
                if (!HasStateAuthority) // The owner starts its own blend when it climbs.
                    BeginClimbBlend(_lastFreePosition);
            }
        }

        private void BeginClimbBlend(Vector3 from)
        {
            _climbFrom = from;
            _climbStartTime = Time.time;
            _climbing = true;
        }

        // Eases from where the gnome stood up to the seat, then sticks exactly to the seat.
        private Vector3 ClimbBlend(Vector3 seat)
        {
            if (!_climbing)
                return seat;

            var t = _climbTime > 0f ? (Time.time - _climbStartTime) / _climbTime : 1f;
            if (t >= 1f)
            {
                _climbing = false;
                return seat;
            }

            return Vector3.Lerp(_climbFrom, seat, 1f - (1f - t) * (1f - t));
        }

        private Vector3 Posed(int depth)
        {
            var mount = depth <= _maxTowerHeight ? Mount : null;
            return mount != null ? mount.Posed(depth + 1) + Vector3.up * mount._seatHeight : transform.position;
        }

        // The rider has no collider of its own (it would push the mount and block raycasts) while it sits on the seat.
        private void SetRidingBody(bool riding)
        {
            if (_controller.enabled == riding)
                _controller.enabled = !riding;
        }

        // Riders have no collider and the base's is thin, so aim by distance from the camera ray to each gnome's body.
        private void UpdateAim()
        {
            _aimed = null;
            var view = FirstPersonCamera.Instance;
            if (view == null || IsRiding || !MatchManager.AllowsInteraction || _character.Role != PlayerRole.Gnome)
                return;

            var origin = view.transform.position;
            var direction = view.transform.forward;
            var bestAlong = float.MaxValue;
            foreach (var other in _all)
            {
                if (other == this || other._character.Role != PlayerRole.Gnome)
                    continue;

                var feet = other.PosedPosition;
                var along = ClosestOnRay(origin, direction, feet, feet + Vector3.up * _bodyHeight, out var onBody);
                if (along >= bestAlong || (origin + direction * along - onBody).sqrMagnitude > _aimRadius * _aimRadius)
                    continue;

                // Aiming at the feet would test the ground; look at least a little above the root.
                onBody.y = Mathf.Max(onBody.y, feet.y + Mathf.Min(MinSightHeight, _bodyHeight));
                if (HasLineOfSight(origin, onBody, other))
                {
                    bestAlong = along;
                    _aimed = other;
                }
            }
        }

        // Closest point of the segment a-b to the ray segment [origin, origin + direction * reach]; returns the distance along the ray.
        private float ClosestOnRay(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, out Vector3 onSegment)
        {
            var segment = b - a;
            var offset = origin - a;
            var segmentSqr = segment.sqrMagnitude;
            var f = Vector3.Dot(segment, offset);
            var c = Vector3.Dot(direction, offset);
            var bDot = Vector3.Dot(direction, segment);
            var denominator = segmentSqr - bDot * bDot;

            // Closest points of two lines (the ray has unit length per unit of s), then clamped to both segments.
            var s = denominator > 1e-6f ? Mathf.Clamp((bDot * f - c * segmentSqr) / denominator, 0f, _reach) : 0f;
            var t = segmentSqr > 1e-6f ? Mathf.Clamp01((bDot * s + f) / segmentSqr) : 0f;
            s = Mathf.Clamp(bDot * t - c, 0f, _reach);

            onSegment = a + segment * t;
            return s;
        }

        // Nothing may stand between the camera and the gnome except its own tower and the local player.
        private bool HasLineOfSight(Vector3 origin, Vector3 target, GnomeStack other)
        {
            var toTarget = target - origin;
            var distance = toTarget.magnitude;
            if (distance < 0.001f)
                return true;

            var count = Physics.RaycastNonAlloc(origin, toTarget / distance, _hits, Mathf.Max(0f, distance - SightMargin), _aimMask, QueryTriggerInteraction.Ignore);
            var otherBase = other.Base;
            for (var i = 0; i < count; i++)
            {
                var hitTransform = _hits[i].transform;
                if (hitTransform.IsChildOf(transform))
                    continue;

                var stack = _hits[i].collider.GetComponentInParent<GnomeStack>();
                if (stack == null || stack.Base != otherBase)
                    return false;
            }

            return true;
        }

        // Shared by the HUD and the climb itself. The remount cooldown is not part of it: it would only flicker the prompt.
        private bool EvaluateClimb(GnomeStack aimed, out string reason)
        {
            reason = null;
            if (aimed == null || !_isSpawned)
                return false;

            if (_movement.IsFrozen)
                reason = "Estás petrificado";
            else if (_movement.IsStunned)
                reason = "Estás aturdido";
            else if (_movement.IsCrouching)
                reason = "Estás agachado";
            else if (aimed._movement.IsCrouching)
                reason = "Ese duende está agachado";
            else if (_interactor != null && _interactor.Carried != null)
                reason = "Tienes las manos ocupadas";
            else if (aimed.Base == this)
                reason = "Es tu propia torre";
            else if (TowerHasStunned(aimed))
                reason = "Ese duende está aturdido";
            else
            {
                var height = aimed.TowerHeight;
                var total = height + 1 + RidersAbove;
                if (height >= _maxTowerHeight)
                    reason = $"Torre llena ({height}/{_maxTowerHeight})";
                else if (total > _maxTowerHeight)
                    reason = $"No caben todos ({total}/{_maxTowerHeight})";
            }

            return reason == null;
        }

        private void TryClimb()
        {
            var aimed = Aimed;
            if (aimed == null || Time.time < _nextClimbTime || !MatchManager.AllowsInteraction || !EvaluateClimb(aimed, out _))
                return;

            BeginClimbBlend(transform.position);
            SetMount(aimed.Top);
            SetRidingBody(true);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(_mountFovPunch, _mountFovPunchTime);
        }

        // Getting off with a jump: clearing the mount first makes PlayerMovement simulate the hop on this tick.
        private void Dismount()
        {
            var view = FirstPersonCamera.Instance;
            var forward = Vector3.ProjectOnPlane(view != null ? view.transform.forward : transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;

            ClearMount();
            _movement.ApplyKnockback(forward * _dismountForward + Vector3.up * _dismountUp, _dismountStun);
        }

        private void SetMount(GnomeStack mount)
        {
            MountRef = mount;
            _movement.IsRiding = mount != null;
            _mountHadMount = mount != null && mount.Mount != null && !ChainContains(mount, this);
        }

        // Owner only. The controller comes back right away so the hop that may follow can move the body.
        private void ClearMount()
        {
            SetMount(null);
            _climbing = false;
            _nextClimbTime = Time.time + _remountCooldown;
            SetRidingBody(false);
        }

        // The base of the tower (the only one with a collider) was kicked: everyone above falls.
        private bool TowerToppled(GnomeStack mount)
        {
            var current = mount;
            for (var i = 0; i <= _maxTowerHeight && current != null; i++)
            {
                if (current._kickable != null && current._kickable.IsStunned)
                    return true;

                current = current.Mount;
            }

            return false;
        }

        private static bool TowerHasStunned(GnomeStack gnome)
        {
            // Everyone above a stunned gnome would be toppled the moment we climb, so look at the whole tower.
            var current = gnome.Top;
            for (var i = 0; i <= gnome._maxTowerHeight + 1 && current != null; i++)
            {
                if (current._kickable != null && current._kickable.IsStunned)
                    return true;

                current = current.Mount;
            }

            return false;
        }

        private Vector3 TopplePushDirection(GnomeStack mount)
        {
            // The camper kicked the tower, so everyone falls the way the kick pushes: from the nearest camper through the base.
            var basePosition = mount.Base.PosedPosition;
            Vector3? camper = null;
            var nearest = float.MaxValue;
            foreach (var character in PlayerCharacter.All)
            {
                var distance = (character.transform.position - basePosition).sqrMagnitude;
                if (character.Role == PlayerRole.Camper && distance < nearest)
                {
                    nearest = distance;
                    camper = character.transform.position;
                }
            }

            var away = camper.HasValue ? Vector3.ProjectOnPlane(basePosition - camper.Value, Vector3.up) : Vector3.zero;
            if (away.sqrMagnitude < 0.0001f)
            {
                var random = Random.insideUnitCircle.normalized;
                return new Vector3(random.x, 0f, random.y);
            }

            return Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;
        }

        // Two gnomes climbed on the same one in the same tick: the one with the lowest player id keeps the seat.
        private bool LosesRace(GnomeStack mount)
        {
            var id = OwnerId;
            foreach (var other in _all)
            {
                if (other != this && other.Mount == mount && other.OwnerId < id)
                    return true;
            }

            return false;
        }

        // Two gnomes climbed on each other in the same tick: the one with the highest player id gets off.
        // True if walking down from start, through the mounts, reaches target (a cycle partner is not a gnome that fell).
        private bool ChainContains(GnomeStack start, GnomeStack target)
        {
            var current = start;
            for (var i = 0; i <= _maxTowerHeight + 1 && current != null; i++)
            {
                if (current == target)
                    return true;

                current = current.Mount;
            }

            return false;
        }

        private bool LosesCycle()
        {
            var highest = OwnerId;
            var current = Mount;
            for (var i = 0; i <= _maxTowerHeight + 1 && current != null; i++)
            {
                if (current == this)
                    return OwnerId >= highest;

                highest = Mathf.Max(highest, current.OwnerId);
                current = current.Mount;
            }

            return false;
        }
    }
}
