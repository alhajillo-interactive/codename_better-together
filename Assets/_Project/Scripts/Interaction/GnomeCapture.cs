using System.Collections.Generic;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Player;
using BetterTogether.UI;
using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// A gnome that the camper can carry while it is stone and lock in the <see cref="GnomeCage"/>.
    /// It holds no networked state: whether this gnome is carried or caged is derived on every peer from what the
    /// other authorities replicate (the camper's <see cref="GnomeCarrier"/> id and the cage's id). The gnome's own owner
    /// is the only one that moves its body (to the camper's anchor, into the cage, back out), so no peer ever writes
    /// another peer's state or transform. Every peer only turns off the gnome's collisions while it is held.
    /// </summary>
    [RequireComponent(typeof(Petrifiable), typeof(PlayerMovement))]
    [DefaultExecutionOrder(-250)]
    public class GnomeCapture : NetworkBehaviour, IInteractable
    {
        private enum CaptureState
        {
            Free,
            Carried,
            Caged,
        }

        [Header("Dropping")]
        [Tooltip("Metres in front of the camper where a dropped gnome is put down (further spots are tried if this one is blocked).")]
        [SerializeField, Min(0f)] private float _dropDistance = 0.9f;
        [Tooltip("Extra metres added to the drop distance for each further attempt when a spot is blocked.")]
        [SerializeField, Min(0.05f)] private float _dropStep = 0.3f;
        [Tooltip("Number of spots tried in front of the camper before falling back to the ground below the gnome.")]
        [SerializeField, Range(1, 8)] private int _dropAttempts = 4;
        [Tooltip("Geometry a dropped gnome stands on and may not overlap. Empty = Environment + GnomeOnly, chosen in Awake.")]
        [SerializeField] private LayerMask _geometryMask;
        [Tooltip("Players a dropped gnome may not overlap. Empty = Player + Gnome, chosen in Awake.")]
        [SerializeField] private LayerMask _playerMask;

        [Header("Escape")]
        [Tooltip("Upper bound in seconds of how far ahead the camper is extrapolated when the gnome breaks free (latency + interpolation).")]
        [SerializeField, Min(0f)] private float _maxLead = 0.3f;
        [Tooltip("Seconds a remote body is shown behind its real position by interpolation, added to half the round trip.")]
        [SerializeField, Min(0f)] private float _interpolationDelay = 0.1f;
        [Tooltip("Degrees to the side of the camper's facing that a freed gnome hops towards (the side alternates).")]
        [SerializeField, Range(0f, 120f)] private float _hopSideAngle = 70f;

        [Header("Release presentation")]
        [Tooltip("Seconds a peer keeps the drawn gnome at the camper's drop spot waiting for the replicated pose to arrive.")]
        [SerializeField, Min(0f)] private float _dropWaitMax = 0.5f;
        [Tooltip("Metres from the drop spot at which the replicated pose counts as arrived.")]
        [SerializeField, Min(0.01f)] private float _dropArrivedDistance = 0.3f;
        [Tooltip("Seconds the drawn gnome takes to glide from the camper's hands to its real pose when it stops being carried.")]
        [SerializeField, Min(0f)] private float _releaseBlend = 0.15f;
        [Tooltip("Metres the drawn gnome rises at the middle of that glide.")]
        [SerializeField, Min(0f)] private float _releaseArc = 0.12f;
        [Tooltip("Camera FOV punch (degrees) on the camper when the gnome slips out of its hands.")]
        [SerializeField] private float _escapeFovPunch = 3f;
        [Tooltip("Camera roll punch (degrees) on the camper when the gnome slips out of its hands.")]
        [SerializeField] private float _escapeRollPunch = 1.5f;
        [Tooltip("Seconds the escape camera punch takes to settle.")]
        [SerializeField, Min(0.05f)] private float _escapePunchTime = 0.25f;

        [Header("Cage")]
        [Tooltip("Seconds after leaving the cage during which the camper cannot petrify this gnome again.")]
        [SerializeField, Min(0f)] private float _exitImmunity = 3f;

        private static readonly List<GnomeCapture> AllGnomes = new();
        private static readonly Collider[] OverlapBuffer = new Collider[8];

        private PlayerMovement _movement;
        private Petrifiable _petrifiable;
        private CharacterController _controller;
        private PlayerCharacter _character;
        private GnomeStack _stack;
        private bool _isSpawned;
        private bool _collisionsOn = true;
        private bool _lastCarried;
        private bool _lastCaged;

        // Owner only.
        private CaptureState _state;
        private GnomeCarrier _carrier;
        private GnomeCage _cage;
        private Vector3 _lastCarrierPosition;
        private Vector3 _lastCarrierForward;
        private bool _carryStarted;

        // Presentation only (every peer).
        private Transform _visual;
        private Vector3 _visualRestPosition;
        private Quaternion _visualRestRotation;
        private Vector3 _lastAnchorPosition;
        private Quaternion _lastAnchorRotation;
        private bool _hasAnchor;
        private bool _wasCarriedByLocal;
        private bool _blending;
        private bool _blendFresh;
        private GnomeCarrier _lastCarrierSeen;
        private bool _blendToDrop;
        private bool _dropBlendFresh;
        private Vector3 _dropPoint;
        private Quaternion _dropRotation;
        private Vector3 _blendFromPosition;
        private Quaternion _blendFromRotation;
        private float _blendStart;
        private Vector3 _blendOffset;
        private Quaternion _blendTurn;

        /// <summary>Every spawned gnome with this component.</summary>
        public static IReadOnlyList<GnomeCapture> All => AllGnomes;

        /// <summary>The spawned gnome with this network object id, or null. Safe to call at any time.</summary>
        public static GnomeCapture Find(NetworkId id)
        {
            if (!id.IsValid)
                return null;

            for (var i = 0; i < AllGnomes.Count; i++)
            {
                var gnome = AllGnomes[i];
                if (gnome._isSpawned && gnome.Object != null && gnome.Object.IsValid && gnome.Object.Id == id)
                    return gnome;
            }

            return null;
        }

        /// <summary>Replicated stone flag. Safe to read at any time.</summary>
        public bool IsStoneNow => _isSpawned && _petrifiable.IsStone;

        /// <summary>Carried by the camper right now (replicated state of the camper, so every peer agrees). Safe at any time.</summary>
        public bool IsCarried => _isSpawned && !IsCaged && IsStoneNow && GnomeCarrier.FindCarrying(Object.Id) != null;

        /// <summary>Locked in the cage right now (replicated state of the cage). Safe at any time.</summary>
        public bool IsCaged => _isSpawned && GnomeCage.IsCaged(Object.Id);

        /// <summary>Carried or caged: cannot move, interact or be part of a tower.</summary>
        public bool IsCaptured => IsCarried || IsCaged;

        /// <summary>Whether the camper may pick this gnome up (the camper adds distance and empty-hands checks).</summary>
        public bool CanBeCarried
        {
            get
            {
                if (!_isSpawned || !IsStoneNow || IsCaptured)
                    return false;

                // A gnome in a tower stays there; so does the one holding up riders.
                return _stack == null || (!_stack.IsRiding && _stack.RidersAbove == 0);
            }
        }

        public string DisplayName => _character != null ? _character.DisplayName : "Duende";

        /// <summary>Distance in metres from a point to this gnome's body.</summary>
        public float DistanceFrom(Vector3 point)
        {
            var closest = _controller != null ? _controller.ClosestPoint(point) : transform.position;
            return Vector3.Distance(closest, point);
        }

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _petrifiable = GetComponent<Petrifiable>();
            _controller = GetComponent<CharacterController>();
            _character = GetComponent<PlayerCharacter>();
            TryGetComponent(out _stack);
            if (_geometryMask.value == 0)
                _geometryMask = LayerMask.GetMask("Environment", "GnomeOnly");
            if (_playerMask.value == 0)
                _playerMask = LayerMask.GetMask("Player", "Gnome");
        }

        public override void Spawned()
        {
            _isSpawned = true;
            AllGnomes.Add(this);
            _state = CaptureState.Free;
            // Late joiners start from whatever is already replicated: no announcements for the past.
            _lastCarried = IsCarried;
            _lastCaged = IsCaged;
            _blending = false;
            _hasAnchor = false;
            _visual = _petrifiable.Visual;
            if (_visual != null)
            {
                _visualRestPosition = _visual.localPosition;
                _visualRestRotation = _visual.localRotation;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            AllGnomes.Remove(this);
            EndBlend();
            SetCollisions(true);
            if (_movement != null)
                _movement.IsCaptured = false;
        }

        // ---- Prompt (mirror of the camper's pickup rule) ----

        public string GetPrompt(PlayerInteractor interactor) => $"Cargar a {DisplayName}";

        public bool CanInteract(PlayerInteractor interactor) =>
            interactor.TryGetComponent<GnomeCarrier>(out var carrier) && carrier.CanCarry(this);

        public void Interact(PlayerInteractor interactor)
        {
            if (interactor.TryGetComponent<GnomeCarrier>(out var carrier))
                carrier.RequestPickUp(this);
        }

        // ---- Owner: places its own body from the replicated state of the camper and the cage ----

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var cage = GnomeCage.FindHolding(Object.Id);
            var carrier = cage == null && _petrifiable.IsStone ? GnomeCarrier.FindCarrying(Object.Id) : null;
            var next = cage != null ? CaptureState.Caged : carrier != null ? CaptureState.Carried : CaptureState.Free;

            if (next != _state)
                Transition(next, carrier, cage);

            switch (_state)
            {
                case CaptureState.Carried:
                    FollowCarrier(carrier);
                    break;
                case CaptureState.Caged:
                    HoldInCage();
                    break;
            }
        }

        // The owner draws its own body every frame at the anchor (the tick rate alone would look jittery to the carried player).
        private void LateUpdate()
        {
            if (!_isSpawned)
                return;

            DrawCarried();
            BlendVisual();
        }

        private void DrawCarried()
        {
            // Every peer draws the carried gnome in the camper's hands (presentation only: the NetworkTransform
            // rewrites a proxy's pose on the next Render, and the owner's own pose is the one it replicates).
            if (HasStateAuthority && (_state != CaptureState.Carried || !_carryStarted || !_petrifiable.IsStone))
                return;

            var carrier = HasStateAuthority ? _carrier : (IsCarried ? GnomeCarrier.FindCarrying(Object.Id) : null);
            if (carrier != null)
            {
                _lastCarrierSeen = carrier;
                _lastAnchorPosition = carrier.AnchorPosition;
                _lastAnchorRotation = carrier.AnchorRotation;
                _hasAnchor = true;
                transform.SetPositionAndRotation(_lastAnchorPosition, _lastAnchorRotation);
            }
        }

        // After being carried the drawn body glides (with a small arc) from the last hand pose to the real pose
        // instead of snapping; only the visual child moves, never the networked root or the colliders.
        private void BlendVisual()
        {
            if (!_blending)
                return;

            if (_visual == null)
            {
                _blending = false;
                return;
            }

            if (_blendFresh)
            {
                // First frame after the release: the root already holds its final pose, so measure the gap now.
                _blendFresh = false;
                _blendOffset = _lastAnchorPosition - transform.position;
                _blendTurn = _lastAnchorRotation * Quaternion.Inverse(transform.rotation);
            }

            var elapsed = Time.time - _blendStart;
            var k = _releaseBlend > 0f ? elapsed / _releaseBlend : 1f;
            var parent = _visual.parent;
            var naturalPosition = parent != null ? parent.TransformPoint(_visualRestPosition) : _visualRestPosition;
            var naturalRotation = parent != null ? parent.rotation * _visualRestRotation : _visualRestRotation;

            if (_blendToDrop)
            {
                // Put down by the camper: this peer's replicated pose still trails behind, so the drawn body goes to the
                // camper's drop spot and waits there until the real pose arrives (or a timeout).
                var toDrop = _dropRotation * Quaternion.Inverse(transform.rotation);
                var targetPosition = _dropPoint + toDrop * (naturalPosition - transform.position);
                var targetRotation = toDrop * naturalRotation;
                if (_dropBlendFresh)
                {
                    _dropBlendFresh = false;
                    _blendFromPosition = naturalPosition + _blendOffset;
                    _blendFromRotation = _blendTurn * naturalRotation;
                }

                var arrived = (transform.position - _dropPoint).sqrMagnitude <= _dropArrivedDistance * _dropArrivedDistance;
                if (k >= 1f && (arrived || elapsed >= _releaseBlend + _dropWaitMax))
                {
                    EndBlend();
                    return;
                }

                var dropEase = Mathf.Clamp01(k);
                dropEase = dropEase * dropEase * (3f - 2f * dropEase);
                var arc = k < 1f ? _releaseArc * Mathf.Sin(Mathf.PI * k) : 0f;
                _visual.SetPositionAndRotation(Vector3.Lerp(_blendFromPosition, targetPosition, dropEase) + Vector3.up * arc,
                    Quaternion.Slerp(_blendFromRotation, targetRotation, dropEase));
                return;
            }

            if (k >= 1f)
            {
                EndBlend();
                return;
            }

            var ease = k * k * (3f - 2f * k);
            var position = naturalPosition + _blendOffset * (1f - ease) + Vector3.up * (_releaseArc * Mathf.Sin(Mathf.PI * k));
            _visual.SetPositionAndRotation(position, Quaternion.Slerp(_blendTurn, Quaternion.identity, ease) * naturalRotation);
        }

        private void EndBlend()
        {
            if (_blending && _visual != null)
            {
                _visual.localPosition = _visualRestPosition;
                _visual.localRotation = _visualRestRotation;
            }

            _blending = false;
            _blendToDrop = false;
            _hasAnchor = false;
        }

        private void Transition(CaptureState next, GnomeCarrier carrier, GnomeCage cage)
        {
            var previous = _state;
            _state = next;
            _carrier = carrier;
            _cage = cage != null ? cage : _cage;

            switch (next)
            {
                case CaptureState.Carried:
                    _movement.IsCaptured = true;
                    _carryStarted = false;
                    break;

                case CaptureState.Caged:
                    _movement.IsCaptured = true;
                    // Out of the stone (it is not needed any more) and into the cage; Petrifiable.IsLockedInCage keeps it immune meanwhile.
                    _petrifiable.ReleaseForCage(cage.CageSeconds + _exitImmunity);
                    _movement.Teleport(cage.InsidePoint.position, cage.InsidePoint.rotation);
                    break;

                default:
                    _movement.IsCaptured = false;
                    if (previous == CaptureState.Caged)
                        LeaveCage();
                    else if (previous == CaptureState.Carried && _petrifiable.IsStone)
                        PutDown(); // Dropped (or the camper left): still stone, so it is set on the ground.
                    // Otherwise it broke free while carried: its own release hop already moves it.
                    break;
            }
        }

        private void FollowCarrier(GnomeCarrier carrier)
        {
            var position = carrier.AnchorPosition;
            var rotation = carrier.AnchorRotation;
            _lastCarrierPosition = carrier.transform.position;
            _lastCarrierForward = carrier.transform.forward;
            if (!_carryStarted)
            {
                // The first tick jumps to the camper's hands without interpolating across the map.
                _carryStarted = true;
                _movement.Teleport(position, rotation);
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void HoldInCage()
        {
            if (_cage == null)
                return;

            // Nothing moves a caged gnome, but a stray push (another system) must not leave it outside.
            var inside = _cage.InsidePoint.position;
            if ((transform.position - inside).sqrMagnitude > 0.0004f)
                _movement.Teleport(inside, _cage.InsidePoint.rotation);
        }

        private void LeaveCage()
        {
            if (_cage != null)
                _movement.Teleport(_cage.ExitPoint.position, _cage.ExitPoint.rotation);

            _petrifiable.ForceRelease(_exitImmunity);
            _cage = null;
        }

        // Puts the gnome down where the camper decided (its replicated drop spot). Without one (the camper left),
        // it falls back to the last carrier pose this peer saw.
        private void PutDown()
        {
            GetBodySize(out var radius, out var height);
            if (_carrier != null && _carrier.TryGetDrop(Object.Id, out var point, out var yaw) && IsFree(point, radius, height))
            {
                _movement.Teleport(point, Quaternion.Euler(0f, yaw, 0f));
                return;
            }

            ComputeDropPoint(_lastCarrierPosition, _lastCarrierForward, out var spot, out var spotYaw);
            _movement.Teleport(spot, Quaternion.Euler(0f, spotYaw, 0f));
        }

        /// <summary>
        /// A safe spot to put this gnome down in front of a camper standing at <paramref name="camperPosition"/> facing
        /// <paramref name="camperForward"/>: on the ground, never inside geometry or another player.
        /// Uses only this gnome's size and the physics world, so any peer can compute it.
        /// </summary>
        public void ComputeDropPoint(Vector3 camperPosition, Vector3 camperForward, out Vector3 point, out float yaw)
        {
            var forward = Vector3.ProjectOnPlane(camperForward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            yaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;
            GetBodySize(out var radius, out var height);

            for (var i = 0; i < _dropAttempts; i++)
            {
                var candidate = camperPosition + forward * (_dropDistance + i * _dropStep);
                if (TryGround(candidate, out var ground) && IsFree(ground, radius, height))
                {
                    point = ground;
                    return;
                }
            }

            // Everything in front is blocked: the ground under the camper (gravity does the rest).
            point = TryGround(camperPosition, out var below) ? below : camperPosition;
        }

        /// <summary>
        /// Owner only, called when this carried gnome breaks free: puts it beside where the camper is "now"
        /// (its delayed body plus velocity times the estimated delay) and returns the horizontal direction to hop in,
        /// out and to a side of the camper, never backwards. False if nobody carries it (the old hop applies).
        /// </summary>
        public bool TryPlaceForEscape(out Vector3 direction)
        {
            direction = default;
            if (!_isSpawned || !HasStateAuthority)
                return false;

            var carrier = GnomeCarrier.FindCarrying(Object.Id);
            if (carrier == null)
                return false;

            var rtt = 0.0;
            if (Runner != null)
            {
                var camper = carrier.Object.StateAuthority;
                rtt = camper != PlayerRef.None ? Runner.GetPlayerRtt(camper) : 0.0;
                if (rtt <= 0.0)
                    rtt = Runner.GetPlayerRtt(Runner.LocalPlayer); // No direct estimate for that peer: use ours.
            }

            var lead = Mathf.Clamp((float)rtt * 0.5f + _interpolationDelay, 0f, _maxLead);
            var origin = carrier.transform.position + carrier.Velocity * lead;
            var facing = Vector3.ProjectOnPlane(carrier.transform.forward, Vector3.up);
            facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : transform.forward;
            GetBodySize(out var radius, out var height);

            var firstSide = (_petrifiable.ReleaseCount & 1) == 0 ? 1f : -1f;
            var rotation = Quaternion.LookRotation(facing, Vector3.up);
            direction = Quaternion.Euler(0f, _hopSideAngle * firstSide, 0f) * facing;
            var placed = false;
            for (var i = 0; i < _dropAttempts && !placed; i++)
            {
                for (var s = 0; s < 2 && !placed; s++)
                {
                    var side = s == 0 ? firstSide : -firstSide;
                    var candidateDirection = Quaternion.Euler(0f, _hopSideAngle * side, 0f) * facing;
                    var candidate = origin + candidateDirection * (_dropDistance + i * _dropStep);
                    if (TryGround(candidate, out var ground) && IsFree(ground, radius, height))
                    {
                        direction = candidateDirection;
                        _movement.Teleport(ground, rotation);
                        placed = true;
                    }
                }
            }

            // Boxed in on both sides: it stays where the hands were; the hop still goes out to the side.
            if (Telemetry.Enabled)
                Telemetry.Begin("gnome_drop").Str("reason", "escape_hop").Float("lead_ms", lead * 1000f).Int("placed", placed ? 1 : 0).Send();

            return true;
        }

        private void GetBodySize(out float radius, out float height)
        {
            var scale = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y));
            radius = Mathf.Max(0.02f, _controller.radius * scale * 0.9f);
            height = Mathf.Max(radius * 2f, _controller.height * scale);
        }

        private bool TryGround(Vector3 around, out Vector3 ground)
        {
            var origin = around + Vector3.up * 1.5f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 3.5f, _geometryMask, QueryTriggerInteraction.Ignore))
            {
                ground = hit.point;
                return true;
            }

            ground = around;
            return false;
        }

        private bool IsFree(Vector3 feet, float radius, float height)
        {
            var bottom = feet + Vector3.up * (radius + 0.03f);
            var top = feet + Vector3.up * Mathf.Max(radius + 0.03f, height - radius);
            var count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, OverlapBuffer, _geometryMask | _playerMask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (OverlapBuffer[i] != _controller)
                    return false;
            }

            return true;
        }

        // ---- Presentation, every peer, from replicated state ----

        public override void Render()
        {
            if (!_isSpawned)
                return;

            var carried = IsCarried;
            var caged = IsCaged;

            // A held gnome neither blocks the camper's walking nor anyone else's; it is back to normal when released.
            SetCollisions(!(carried || caged));

            if (HasStateAuthority)
            {
                if (carried && !_lastCarried)
                    Announce("¡Te han atrapado!");
                else if (caged && !_lastCaged)
                    Announce("En la jaula: tus amigos pueden liberarte");
            }

            if (carried)
                _wasCarriedByLocal = GnomeCarrier.FindCarrying(Object.Id) == GnomeCarrier.Local;

            // Stopped being carried (put down, escaped, camper left): glide the drawn body instead of snapping.
            // Caging is a deliberate move to the cage, not a release.
            if (_lastCarried && !carried && !caged)
            {
                if (_hasAnchor && _visual != null && _releaseBlend > 0f)
                {
                    _blending = true;
                    _blendFresh = true;
                    _dropBlendFresh = true;
                    _blendToDrop = false;
                    _blendStart = Time.time;

                    // A peer that does not own the gnome follows the camper's drop spot (its own pose arrives late).
                    if (!HasStateAuthority && IsStoneNow && _lastCarrierSeen != null
                        && _lastCarrierSeen.TryGetDrop(Object.Id, out var dropPoint, out var dropYaw))
                    {
                        _blendToDrop = true;
                        _dropPoint = dropPoint;
                        _dropRotation = Quaternion.Euler(0f, dropYaw, 0f);
                    }
                }

                // The camper feels it when the gnome slips out of its hands (not when it puts the gnome down itself).
                if (_wasCarriedByLocal && !IsStoneNow && CameraPunch.Instance != null)
                {
                    CameraPunch.Instance.PunchFov(_escapeFovPunch, _escapePunchTime);
                    CameraPunch.Instance.PunchRoll(Random.value < 0.5f ? -_escapeRollPunch : _escapeRollPunch, _escapePunchTime);
                }
            }

            if (!carried)
                _wasCarriedByLocal = false;

            _lastCarried = carried;
            _lastCaged = caged;
        }

        private static void Announce(string text) => MatchHUD.AnnounceLocal(text, new Color(1f, 0.55f, 0.35f));

        private void SetCollisions(bool on)
        {
            if (_controller == null || _collisionsOn == on)
                return;

            _collisionsOn = on;
            _controller.detectCollisions = on;
        }
    }
}
