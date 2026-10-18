using System.Collections.Generic;
using BetterTogether.Player;
using BetterTogether.Match;
using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// An object players can pick up and put down. The camper carries anything alone; big items need several gnomes.
    /// Master-client object: the state authority is the single arbiter of grabs and releases (via RPCs),
    /// and every client poses the item on its holders' carry anchors. The item is a Rigidbody synced by
    /// NetworkTransform: the authority keeps it kinematic while carried and lets physics drop it otherwise.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Carryable : NetworkBehaviour, IInteractable, IMatchResettable
    {
        private static readonly List<Carryable> All = new();

        [SerializeField] private string _displayName = "objeto";
        [Tooltip("Gnomes needed to move it (1 or 2). The camper always carries it alone.")]
        [SerializeField, Range(1, 2)] private int _gnomesRequired = 1;
        [Tooltip("Two gnomes farther apart than this drop the item.")]
        [SerializeField] private float _maxGnomeSpan = 1.6f;
        [Tooltip("A lone gnome waiting for help that gets farther than this from the item lets go.")]
        [SerializeField] private float _maxSoloGrabDistance = 1.2f;

        private Collider[] _colliders;
        private Rigidbody _body;
        private NetworkTransform _networkTransform;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private bool _collidersEnabled = true;
        private bool _isSpawned;

        [Networked] private PlayerInteractor HolderA { get; set; }
        [Networked] private PlayerInteractor HolderB { get; set; }
        [Networked] private NetworkBehaviourId PinOwnerId { get; set; }

        /// <summary>True while the item follows its holders.</summary>
        public bool IsMoving
        {
            get
            {
                var count = HolderCount;
                if (count == 1)
                    return RequiredHolders(Holder(0)) == 1;
                return count == 2 && HeldByGnomes;
            }
        }

        /// <summary>True while someone holds the item, even if it can't move yet.</summary>
        public bool IsHeld => _isSpawned && HolderCount > 0;

        /// <summary>True while the item rests on an <see cref="IPinSpot"/>, e.g. a grill.</summary>
        public bool IsPinned => _isSpawned && PinOwnerId.IsValid;

        /// <summary>
        /// When set, the item has no colliders, can't be grabbed and stays frozen (e.g. an eaten sausage).
        /// Set it on every peer; it is not networked.
        /// </summary>
        public bool Inert { get; set; }

        private int HolderCount => (Valid(HolderA) != null ? 1 : 0) + (Valid(HolderB) != null ? 1 : 0);
        private bool HeldByGnomes => HolderCount > 0 && Holder(0).Role == PlayerRole.Gnome;

        /// <summary>The item an interactor is holding, if any.</summary>
        public static Carryable FindHeldBy(PlayerInteractor interactor)
        {
            foreach (var carryable in All)
            {
                if (carryable.IsHeldBy(interactor))
                    return carryable;
            }

            return null;
        }

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _startPosition = transform.position;
            _startRotation = transform.rotation;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            All.Remove(this);
        }

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (HolderCount > 0)
                return $"Ayudar a cargar {_displayName}";

            return interactor.Role == PlayerRole.Camper || _gnomesRequired == 1
                ? $"Tomar {_displayName}"
                : $"Agarrar {_displayName} ({_gnomesRequired} duendes)";
        }

        /// <summary>Mirrors the authority's rules using the replicated state.</summary>
        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!_isSpawned || !MatchManager.AllowsInteraction)
                return false;

            if (Inert || FindHeldBy(interactor) != null)
                return false;

            return HolderCount == 0
                || (HeldByGnomes && interactor.Role == PlayerRole.Gnome && HolderCount < _gnomesRequired);
        }

        public void Interact(PlayerInteractor interactor) => RPC_RequestGrab(interactor.Id);

        public void RequestRelease(PlayerInteractor who) => RPC_RequestRelease(who.Id, false, default);

        /// <summary>Releases the item and moves it to <paramref name="dropPoint"/> first (e.g. knocked out of a kicked gnome's hands).</summary>
        public void RequestRelease(PlayerInteractor who, Vector3 dropPoint) => RPC_RequestRelease(who.Id, true, dropPoint);

        /// <summary>Authority only: makes every holder let go.</summary>
        public void ForceReleaseAll()
        {
            HolderA = null;
            HolderB = null;
        }

        /// <summary>Authority only: rests the item on the spot, kinematic, until someone grabs it.</summary>
        public void Pin<T>(T spot) where T : NetworkBehaviour, IPinSpot
        {
            PinOwnerId = spot.Id;
            _body.isKinematic = true;
            transform.SetPositionAndRotation(spot.PinPoint.position, spot.PinPoint.rotation);
        }

        /// <summary>Authority only: lets the item go back to physics.</summary>
        public void Unpin() => PinOwnerId = default;

        /// <summary>Authority only: drops the item and puts it back where it started.</summary>
        public void ResetForMatch()
        {
            ForceReleaseAll();
            Unpin();

            _body.isKinematic = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;

            transform.SetPositionAndRotation(_startPosition, _startRotation);
            if (_networkTransform != null)
                _networkTransform.Teleport(_startPosition, _startRotation);
        }

        /// <summary>Whether the item currently rests on this spot.</summary>
        public bool IsPinnedTo(NetworkBehaviour spot) => IsPinned && PinOwnerId.Equals(spot.Id);

        /// <summary>Hint for a holder that cannot move the item yet, or null.</summary>
        public string StatusFor(PlayerInteractor who) =>
            IsHeldBy(who) && !IsMoving ? "Esperando otro duende..." : null;

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestGrab(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanInteract(who))
                return;

            Unpin();
            Compact();
            if (Valid(HolderA) == null)
                HolderA = who;
            else
                HolderB = who;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestRelease(NetworkBehaviourId interactorId, NetworkBool hasDropPoint, Vector3 dropPoint)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !IsHeldBy(who))
                return;

            // Keep the carry height; physics takes it to the ground once released.
            if (hasDropPoint && IsMoving)
                transform.position = new Vector3(dropPoint.x, transform.position.y, dropPoint.z);

            if (HolderA == who)
                HolderA = null;
            else
                HolderB = null;

            Compact();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            Compact();
            if (HolderCount == 2 && HorizontalDistance(Holder(0).transform.position, Holder(1).transform.position) > _maxGnomeSpan)
            {
                HolderA = null;
                HolderB = null;
            }
            else if (HolderCount == 1 && !IsMoving && DistanceToHolder(Holder(0)) > _maxSoloGrabDistance)
            {
                HolderA = null;
            }

            // Kinematic while carried; dynamic (falls under gravity) otherwise. Proxies are left to NetworkTransform.
            var shouldBeKinematic = IsMoving || IsPinned || Inert;
            if (_body.isKinematic != shouldBeKinematic)
            {
                _body.isKinematic = shouldBeKinematic;
                if (!shouldBeKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }
            }

            if (TryGetFollowPose(out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        public override void Render()
        {
            var shouldEnable = !IsMoving && !Inert;
            if (shouldEnable == _collidersEnabled)
                return;

            _collidersEnabled = shouldEnable;
            foreach (var c in _colliders)
                c.enabled = shouldEnable;
        }

        // Runs after NetworkTransform has rendered the replicated pose.
        private void LateUpdate()
        {
            // LateUpdate also runs before Spawned / after Despawned, when networked state can't be read.
            if (_isSpawned && TryGetFollowPose(out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        private bool TryGetFollowPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = transform.rotation;
            if (!IsMoving)
                return TryGetPinPose(out position, out rotation);

            var first = Holder(0).CarryAnchor;
            if (HolderCount == 1)
            {
                position = first.position;
                rotation = first.rotation;
                return true;
            }

            var second = Holder(1).CarryAnchor;
            position = (first.position + second.position) * 0.5f;
            var across = Vector3.ProjectOnPlane(second.position - first.position, Vector3.up);
            if (across.sqrMagnitude > 1e-4f)
                rotation = Quaternion.AngleAxis(Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg, Vector3.up);
            return true;
        }

        private bool TryGetPinPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = transform.rotation;
            if (Inert || !IsPinned || Runner == null || !Runner.TryFindBehaviour(PinOwnerId, out NetworkBehaviour owner) || owner is not IPinSpot spot)
                return false;

            var point = spot.PinPoint;
            position = point.position;
            rotation = point.rotation;
            return true;
        }

        /// <summary>Horizontal distance from the holder to the closest point of the item.</summary>
        private float DistanceToHolder(PlayerInteractor holder)
        {
            var from = holder.transform.position;
            var best = HorizontalDistance(transform.position, from);
            foreach (var c in _colliders)
                best = Mathf.Min(best, HorizontalDistance(c.ClosestPoint(from), from));
            return best;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y;
            return Vector3.Distance(a, b);
        }

        private int RequiredHolders(PlayerInteractor who) => who.Role == PlayerRole.Camper ? 1 : _gnomesRequired;

        private bool IsHeldBy(PlayerInteractor interactor) =>
            interactor != null && (Valid(HolderA) == interactor || Valid(HolderB) == interactor);

        /// <summary>The i-th live holder; holders that left the game count as empty.</summary>
        private PlayerInteractor Holder(int i)
        {
            var a = Valid(HolderA);
            var b = Valid(HolderB);
            if (a == null)
                return i == 0 ? b : null;
            return i == 0 ? a : b;
        }

        private static PlayerInteractor Valid(PlayerInteractor holder) =>
            holder != null && holder.Object != null && holder.Object.IsValid ? holder : null;

        /// <summary>Authority only: drops dead holders and fills slot A first.</summary>
        private void Compact()
        {
            var a = Holder(0);
            var b = Holder(1);
            if (HolderA != a)
                HolderA = a;
            if (HolderB != b)
                HolderB = b;
        }
    }
}
