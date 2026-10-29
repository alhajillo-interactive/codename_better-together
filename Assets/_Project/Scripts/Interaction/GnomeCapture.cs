using System.Collections.Generic;
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
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            AllGnomes.Remove(this);
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

            // Every peer draws the carried gnome in the camper's hands (presentation only: the NetworkTransform
            // rewrites a proxy's pose on the next Render, and the owner's own pose is the one it replicates).
            if (HasStateAuthority && (_state != CaptureState.Carried || !_carryStarted))
                return;

            var carrier = HasStateAuthority ? _carrier : (IsCarried ? GnomeCarrier.FindCarrying(Object.Id) : null);
            if (carrier != null)
                transform.SetPositionAndRotation(carrier.AnchorPosition, carrier.AnchorRotation);
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
                    // Out of the stone (it is not needed any more) and into the cage; the cage keeps it immune meanwhile.
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

        // Puts the gnome down in front of where the camper was, never inside geometry or another player.
        private void PutDown()
        {
            var forward = Vector3.ProjectOnPlane(_lastCarrierForward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            var scale = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y));
            var radius = Mathf.Max(0.02f, _controller.radius * scale * 0.9f);
            var height = Mathf.Max(radius * 2f, _controller.height * scale);

            for (var i = 0; i < _dropAttempts; i++)
            {
                var candidate = _lastCarrierPosition + forward * (_dropDistance + i * _dropStep);
                if (TryGround(candidate, out var ground) && IsFree(ground, radius, height))
                {
                    _movement.Teleport(ground, Quaternion.LookRotation(forward, Vector3.up));
                    return;
                }
            }

            // Everything in front is blocked: the ground under the current spot (gravity does the rest).
            if (TryGround(transform.position, out var below))
                _movement.Teleport(below, transform.rotation);
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
