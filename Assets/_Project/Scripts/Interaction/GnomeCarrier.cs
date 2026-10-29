using System.Collections.Generic;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// The camper's side of carrying a petrified gnome. The camper is the only writer of <see cref="CarriedGnome"/>
    /// (the id of the gnome it carries); the gnome reads it and moves itself to <see cref="AnchorPosition"/>
    /// (see <see cref="GnomeCapture"/>). Nobody writes another peer's state or transform.
    /// The camper picks a gnome up with the interact key (the prompt comes from <see cref="GnomeCapture"/>), drops it with the
    /// interact key when nothing else is focused, and lets go on its own when the gnome broke free, was caged or left.
    /// Presses are buffered in Update and applied in FixedUpdateNetwork, which owns the state.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    public class GnomeCarrier : NetworkBehaviour
    {
        [Tooltip("Where the carried gnome's feet are held, as a child of the camper root. If empty, the offset below is used.")]
        [SerializeField] private Transform _carryAnchor;
        [Tooltip("Used only without a carry anchor: offset from the camper root (local space) where the carried gnome is held.")]
        [SerializeField] private Vector3 _fallbackOffset = new(0f, 1f, 0.6f);
        [Tooltip("Metres from the camper's eyes to the gnome's body within which a petrified gnome can be picked up.")]
        [SerializeField, Min(0f)] private float _pickupDistance = 2f;

        [Header("Motion tracking")]
        [Tooltip("Speed in m/s above which a position change is treated as a teleport and ignored when estimating the camper's velocity.")]
        [SerializeField, Min(0f)] private float _maxTrackedSpeed = 12f;
        [Tooltip("How fast the estimated velocity follows the measured one (1/seconds); higher = less smoothing.")]
        [SerializeField, Min(0.1f)] private float _velocitySharpness = 12f;

        private static readonly List<GnomeCarrier> AllCarriers = new();

        private PlayerInteractor _interactor;
        private InputAction _interact;
        private bool _isSpawned;
        private bool _dropQueued;
        private NetworkId _pendingPickup;
        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;
        private Vector3 _velocity;

        /// <summary>Id of the carried gnome (default when none). Written only by this camper. Read it through <see cref="Carried"/>.</summary>
        [Networked] private NetworkId CarriedGnome { get; set; }

        /// <summary>Gnome that was put down by the last drop (default if none). Written only by this camper together with the drop point.</summary>
        [Networked] private NetworkId DropGnome { get; set; }

        /// <summary>Where the camper put that gnome down, computed on the camper's real position.</summary>
        [Networked] private Vector3 DropPoint { get; set; }

        /// <summary>Yaw (degrees) the dropped gnome faces.</summary>
        [Networked] private float DropYaw { get; set; }

        public static GnomeCarrier Local { get; private set; }

        /// <summary>
        /// Horizontal velocity of this camper in m/s, derived on every peer from the positions it presents (no network state).
        /// On a remote peer it is the velocity of the delayed, interpolated body.
        /// </summary>
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// The drop spot the camper wrote for this gnome, if the camper's replicated state still says so.
        /// Safe at any time; false before spawn or for any other gnome.
        /// </summary>
        public bool TryGetDrop(NetworkId gnomeId, out Vector3 point, out float yaw)
        {
            point = default;
            yaw = 0f;
            if (!_isSpawned || !gnomeId.IsValid || DropGnome != gnomeId)
                return false;

            point = DropPoint;
            yaw = DropYaw;
            return true;
        }

        /// <summary>The gnome this camper carries according to the replicated state, or null. Safe to read at any time.</summary>
        public GnomeCapture Carried => _isSpawned && CarriedGnome.IsValid ? GnomeCapture.Find(CarriedGnome) : null;

        public bool IsCarrying => Carried != null;

        /// <summary>World position where the carried gnome's feet are held.</summary>
        public Vector3 AnchorPosition => _carryAnchor != null ? _carryAnchor.position : transform.TransformPoint(_fallbackOffset);

        /// <summary>Facing of the carried gnome: the camper's own.</summary>
        public Quaternion AnchorRotation => _carryAnchor != null ? _carryAnchor.rotation : transform.rotation;

        /// <summary>The carrier that currently holds the gnome with this id, or null. Safe to read at any time.</summary>
        public static GnomeCarrier FindCarrying(NetworkId gnomeId)
        {
            if (!gnomeId.IsValid)
                return null;

            for (var i = 0; i < AllCarriers.Count; i++)
            {
                var carrier = AllCarriers[i];
                if (carrier._isSpawned && carrier.CarriedGnome == gnomeId)
                    return carrier;
            }

            return null;
        }

        private void Awake()
        {
            _interactor = GetComponent<PlayerInteractor>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            AllCarriers.Add(this);
            if (!HasStateAuthority)
                return;

            Local = this;
            _interact = InputSystem.actions.FindAction("Player/Interact");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            AllCarriers.Remove(this);
            if (Local == this)
                Local = null;
        }

        /// <summary>
        /// The same rule the prompt shows and the pickup applies: the local camper, empty hands, in reach of a
        /// stone gnome that nobody else holds.
        /// </summary>
        public bool CanCarry(GnomeCapture gnome)
        {
            if (!_isSpawned || gnome == null || !gnome.CanBeCarried || !MatchManager.AllowsInteraction || CarriedGnome.IsValid)
                return false;

            if (_interactor.Role != PlayerRole.Camper || _interactor.Carried != null)
                return false;

            if (this == Local && _interactor.CarriedOrPredicted != null)
                return false;

            if (_interactor.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned)
                return false;

            return gnome.DistanceFrom(_interactor.EyePosition) <= _pickupDistance;
        }

        /// <summary>Local camper only: asks to pick the gnome up; applied (and re-checked) in the next FixedUpdateNetwork.</summary>
        public void RequestPickUp(GnomeCapture gnome)
        {
            if (this == Local && gnome != null && gnome.Object != null && gnome.Object.IsValid)
                _pendingPickup = gnome.Object.Id;
        }

        // Presentation only: measures the velocity of the body as presented, for estimating where it is "now".
        private void LateUpdate()
        {
            if (!_isSpawned)
                return;

            var position = transform.position;
            var dt = Time.deltaTime;
            if (_hasPreviousPosition && dt > 0f)
            {
                var measured = (position - _previousPosition) / dt;
                measured.y = 0f;
                if (measured.magnitude > _maxTrackedSpeed)
                    measured = Vector3.zero; // A teleport, not a run.

                _velocity = Vector3.Lerp(_velocity, measured, 1f - Mathf.Exp(-_velocitySharpness * dt));
            }

            _previousPosition = position;
            _hasPreviousPosition = true;
        }

        // Local input only: nothing networked is written here.
        private void Update()
        {
            if (Local != this || _interact == null || !_isSpawned || !_interact.WasPressedThisFrame() || FirstPersonCamera.IsTyping)
                return;

            // Anything the player is looking at and can use (a cage, an item) takes the key first.
            var focused = _interactor.Focused;
            if (CarriedGnome.IsValid && (focused == null || !focused.CanInteract(_interactor)))
                _dropQueued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var pickup = _pendingPickup;
            _pendingPickup = default;
            if (pickup.IsValid && !CarriedGnome.IsValid)
            {
                var wanted = GnomeCapture.Find(pickup);
                if (wanted != null && CanCarry(wanted))
                {
                    CarriedGnome = pickup;
                    DropGnome = default;
                    _dropQueued = false;
                    if (Telemetry.Enabled)
                        Telemetry.Begin("gnome_carry").Int("gnome", wanted.Object.StateAuthority.PlayerId).Send();
                }
            }

            var carriedId = CarriedGnome;
            if (carriedId.IsValid)
            {
                var gnome = GnomeCapture.Find(carriedId);
                var reason = Release(gnome);
                if (reason != null)
                {
                    // Still stone (put down or reset): this camper decides where it lands, from its own real position,
                    // and the gnome's owner teleports there. Written in the same tick as the id, so they replicate together.
                    if (gnome != null && gnome.IsStoneNow && reason != "cage")
                    {
                        gnome.ComputeDropPoint(transform.position, transform.forward, out var point, out var yaw);
                        DropPoint = point;
                        DropYaw = yaw;
                        DropGnome = carriedId;
                    }

                    CarriedGnome = default;
                    if (Telemetry.Enabled)
                        Telemetry.Begin("gnome_drop").Str("reason", reason).Send();
                }
            }

            _dropQueued = false;
        }

        // Why the gnome is no longer carried, or null while it still is. Escaping (it stopped being stone) and the
        // cage closing are seen on the replicated state of the other peers, never written for them.
        private string Release(GnomeCapture gnome)
        {
            if (gnome == null)
                return "left";
            if (GnomeCage.IsCaged(gnome.Object.Id))
                return "cage";
            if (!gnome.IsStoneNow)
                return "escape";
            if (_dropQueued)
                return "drop";
            var match = MatchManager.Instance;
            if (match != null && match.Phase != MatchPhase.Playing)
                return "reset";
            return null;
        }
    }
}
