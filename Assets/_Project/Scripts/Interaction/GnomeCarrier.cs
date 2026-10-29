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

        private static readonly List<GnomeCarrier> AllCarriers = new();

        private PlayerInteractor _interactor;
        private InputAction _interact;
        private bool _isSpawned;
        private bool _dropQueued;
        private NetworkId _pendingPickup;

        /// <summary>Id of the carried gnome (default when none). Written only by this camper. Read it through <see cref="Carried"/>.</summary>
        [Networked] private NetworkId CarriedGnome { get; set; }

        public static GnomeCarrier Local { get; private set; }

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
