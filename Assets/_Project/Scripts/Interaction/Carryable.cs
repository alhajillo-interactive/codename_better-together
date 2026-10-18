using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
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

        [Header("Hand-off")]
        [Tooltip("Heard by everyone when the item passes from one gnome to another.")]
        [SerializeField] private SoundCue _handOffSfx;

        [Header("Impact")]
        [Tooltip("Heard by everyone, from where it lands, when the item hits something while free (not carried or placed).")]
        [SerializeField] private SoundCue _impactSfx;
        [Tooltip("Minimum impact speed (m/s) that makes a sound.")]
        [SerializeField, Min(0f)] private float _impactMinSpeed = 1.5f;

        [Header("Pose blend")]
        [Tooltip("Seconds the item takes to fly from one holder to the next (local presentation only).")]
        [SerializeField, Min(0f)] private float _handOffBlendTime = 0.18f;
        [Tooltip("Height of the arc the item draws while passing between holders.")]
        [SerializeField, Min(0f)] private float _handOffArc = 0.15f;
        [Tooltip("Seconds the item takes to settle on a pin spot, e.g. a campfire slot.")]
        [SerializeField, Min(0f)] private float _snapBlendTime = 0.28f;
        [Tooltip("Height of the arc the item draws while settling on a pin spot.")]
        [SerializeField, Min(0f)] private float _snapArc = 0.25f;

        private const float ReceiveFovPunch = 2f;
        private const float ReceiveFovPunchTime = 0.12f;

        private enum PoseKind : byte { None, Held, Pinned }

        private Collider[] _colliders;
        private Rigidbody _body;
        private NetworkTransform _networkTransform;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private bool _collidersEnabled = true;
        private bool _isSpawned;
        private PoseKind _lastKind;
        private NetworkBehaviourId _lastKeyId;
        private int _lastKeySlot;
        private Vector3 _drawnPosition;
        private Quaternion _drawnRotation;
        private bool _blending;
        private Vector3 _blendFromPosition;
        private Quaternion _blendFromRotation;
        private float _blendStart;
        private float _blendTime;
        private float _blendArc;
        private float _freeSince = -1f;
        private bool _hasRested;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private PlayerInteractor _lastHolder;
        private int _lastHolderCount;

        [Networked] private PlayerInteractor HolderA { get; set; }
        [Networked] private PlayerInteractor HolderB { get; set; }
        [Networked] private NetworkBehaviourId PinOwnerId { get; set; }
        [Networked] private byte PinSlot { get; set; }

        public string DisplayName => _displayName;

        /// <summary>
        /// State authority only (not networked): someone has held the item since the last match reset. The campfire only
        /// snaps in logs that were handled, so logs lying around the camp at the start do not fill it.
        /// </summary>
        public bool WasHandled { get; private set; }

        /// <summary>
        /// State authority only (not networked): the last player to hold the item was the camper. The campfire only snaps in
        /// logs the camper handled, so a log a gnome drops in the area stays loose.
        /// </summary>
        public bool LastHeldByCamper { get; private set; }

        /// <summary>State authority only: seconds the item has been loose and under physics (no holder, not pinned); 0 otherwise.</summary>
        public float FreeTime => _freeSince < 0f ? 0f : Time.time - _freeSince;

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

        /// <summary>Live holders, 0 to 2.</summary>
        public int HolderCount => !_isSpawned ? 0 : (Valid(HolderA) != null ? 1 : 0) + (Valid(HolderB) != null ? 1 : 0);
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

            // Nobody can take a log back out of the campfire once it is placed.
            if (IsPinned && Runner.TryFindBehaviour(PinOwnerId, out NetworkBehaviour owner) && owner is FireplaceManager)
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

        /// <summary>Authority only: rests the item on the spot (in <paramref name="slot"/> when it has several), kinematic, until someone grabs it.</summary>
        public void Pin<T>(T spot, int slot = 0) where T : NetworkBehaviour, IPinSpot
        {
            PinOwnerId = spot.Id;
            PinSlot = (byte)slot;
            _body.isKinematic = true;
            var point = spot.GetPinPoint(slot);
            transform.SetPositionAndRotation(point.position, point.rotation);
        }

        /// <summary>Authority only: lets the item go back to physics.</summary>
        public void Unpin()
        {
            PinOwnerId = default;
            PinSlot = 0;
        }

        /// <summary>Authority only: drops the item and puts it back where it started.</summary>
        public void ResetForMatch()
        {
            WasHandled = false;
            LastHeldByCamper = false;
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

        /// <summary>Whether the item currently rests on this slot of the spot.</summary>
        public bool IsPinnedTo(NetworkBehaviour spot, int slot) => IsPinnedTo(spot) && PinSlot == slot;

        /// <summary>Slot of the spot the item rests on; only meaningful while <see cref="IsPinnedTo(NetworkBehaviour)"/>.</summary>
        public int PinnedSlot => _isSpawned ? PinSlot : 0;

        /// <summary>Fills <paramref name="results"/> with the spawned items resting on the spot.</summary>
        public static void GetPinnedTo(NetworkBehaviour spot, List<Carryable> results)
        {
            results.Clear();
            foreach (var carryable in All)
            {
                if (carryable.IsPinnedTo(spot))
                    results.Add(carryable);
            }
        }

        /// <summary>Hint for a holder that cannot move the item yet, or null.</summary>
        public string StatusFor(PlayerInteractor who) =>
            IsHeldBy(who) && !IsMoving ? "Esperando otro duende..." : null;

        /// <summary>
        /// Whether <paramref name="from"/> may pass this item to <paramref name="to"/>, a gnome right above or below it in a tower.
        /// <paramref name="reason"/> explains the one case worth telling the player (full hands), otherwise null.
        /// Mirrors the authority's rules using the replicated state.
        /// </summary>
        public bool CanHandOff(PlayerInteractor from, PlayerInteractor to, out string reason)
        {
            reason = null;
            if (!_isSpawned || !MatchManager.AllowsInteraction || Inert || IsPinned || from == null || to == null || from == to)
                return false;

            if (HolderCount != 1 || !IsMoving || !IsHeldBy(from))
                return false;

            if (!from.TryGetComponent<GnomeStack>(out var fromStack) || !to.TryGetComponent<GnomeStack>(out var toStack)
                || from.Role != PlayerRole.Gnome || to.Role != PlayerRole.Gnome)
                return false;

            if (toStack.Mount != fromStack && fromStack.Mount != toStack)
                return false;

            if (IsOutOfAction(from) || IsOutOfAction(to))
                return false;

            if (FindHeldBy(to) != null)
            {
                reason = $"{to.GetComponent<PlayerCharacter>().DisplayName} tiene las manos ocupadas";
                return false;
            }

            return true;
        }

        /// <summary>Passes the item from one gnome to the next one in the tower (see <see cref="CanHandOff"/>).</summary>
        public void RequestHandOff(PlayerInteractor from, PlayerInteractor to) => RPC_RequestHandOff(from.Id, to.Id);

        // Petrified or kick-stunned gnomes can neither give nor take.
        private static bool IsOutOfAction(PlayerInteractor who) =>
            (who.TryGetComponent<Petrifiable>(out var petrifiable) && petrifiable.IsStone)
            || (who.TryGetComponent<Kickable>(out var kickable) && kickable.IsStunned);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestHandOff(NetworkBehaviourId fromId, NetworkBehaviourId toId, RpcInfo info = default)
        {
            if (!Runner.TryFindBehaviour(fromId, out PlayerInteractor from) || !Runner.TryFindBehaviour(toId, out PlayerInteractor to)
                || !CanHandOff(from, to, out _))
                return;

            // Only the giver may pass its own item. A call made on this very peer carries no remote source.
            var giver = from.Object.StateAuthority;
            if (info.Source != giver && !(info.Source == PlayerRef.None && Runner.LocalPlayer == giver))
                return;

            HolderA = to;
            HolderB = null;
        }

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
            if (HolderCount > 0)
            {
                WasHandled = true;
                LastHeldByCamper = Holder(0).Role == PlayerRole.Camper;
            }

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

            // Loose and falling under physics: the campfire waits for it to settle before snapping it in.
            if (HolderCount == 0 && !IsPinned && !_body.isKinematic)
            {
                if (_freeSince < 0f)
                    _freeSince = Time.time;
            }
            else
            {
                _freeSince = -1f;
            }

            if (TryGetFollowPose(out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        // Only the authority simulates the physics; the impact is announced to everyone.
        private void OnCollisionEnter(Collision collision)
        {
            if (!_isSpawned || !HasStateAuthority || _impactSfx == null || HolderCount > 0 || IsPinned || _body.isKinematic)
                return;

            if (collision.relativeVelocity.magnitude >= _impactMinSpeed)
                RPC_PlayImpact(transform.position);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayImpact(Vector3 position) => _impactSfx.PlayAt(position);

        public override void Render()
        {
            UpdateHandOffFeedback();

            // No colliders while the item flies between holders or into a slot, so it does not shove anything on its way.
            var shouldEnable = !IsMoving && !Inert && !_blending;
            if (shouldEnable == _collidersEnabled)
                return;

            _collidersEnabled = shouldEnable;
            foreach (var c in _colliders)
                c.enabled = shouldEnable;
        }

        // The item passing from the only holder to another one: everyone hears it, the receiver also feels it.
        private void UpdateHandOffFeedback()
        {
            var count = HolderCount;
            var holder = Holder(0);
            if (count == 1 && _lastHolderCount == 1 && holder != null && _lastHolder != null && holder != _lastHolder)
            {
                _handOffSfx.PlayAt(transform.position);
                if (holder == PlayerInteractor.Local && CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(ReceiveFovPunch, ReceiveFovPunchTime);
            }

            _lastHolder = holder;
            _lastHolderCount = count;
        }

        // Runs after NetworkTransform has rendered the replicated pose.
        private void LateUpdate()
        {
            // LateUpdate also runs before Spawned / after Despawned, when networked state can't be read.
            if (!_isSpawned)
                return;

            var hasPose = TryGetFollowPose(out var position, out var rotation);
            var kind = PoseKind.None;
            var keyId = default(NetworkBehaviourId);
            var keySlot = 0;
            if (hasPose)
                kind = GetPoseKey(out keyId, out keySlot);

            // Moves between two real poses blend, and so does a resting item settling into a pin spot (auto-snap).
            // Pickups from the ground and resets just happen.
            if (kind != PoseKind.None && _lastKind != PoseKind.None && (kind != _lastKind || !keyId.Equals(_lastKeyId) || keySlot != _lastKeySlot))
                StartBlend(kind == PoseKind.Pinned, _drawnPosition, _drawnRotation);
            else if (kind == PoseKind.Pinned && _lastKind == PoseKind.None && _hasRested)
                StartBlend(true, _restPosition, _restRotation);

            _lastKind = kind;
            _lastKeyId = keyId;
            _lastKeySlot = keySlot;

            if (!hasPose)
            {
                _blending = false;
                _hasRested = true;
                _restPosition = transform.position;
                _restRotation = transform.rotation;
                return;
            }

            if (_blending)
                BlendPose(ref position, ref rotation);

            transform.SetPositionAndRotation(position, rotation);
            _drawnPosition = position;
            _drawnRotation = rotation;
        }

        private PoseKind GetPoseKey(out NetworkBehaviourId id, out int slot)
        {
            if (IsMoving)
            {
                id = Holder(0).Id;
                slot = 0;
                return PoseKind.Held;
            }

            id = PinOwnerId;
            slot = PinSlot;
            return PoseKind.Pinned;
        }

        private void StartBlend(bool toPin, Vector3 fromPosition, Quaternion fromRotation)
        {
            _blending = true;
            _blendFromPosition = fromPosition;
            _blendFromRotation = fromRotation;
            _blendStart = Time.time;
            _blendTime = toPin ? _snapBlendTime : _handOffBlendTime;
            _blendArc = toPin ? _snapArc : _handOffArc;
        }

        // Ease-out cubic along the way, with an arc on top; the real pose is always the destination.
        private void BlendPose(ref Vector3 position, ref Quaternion rotation)
        {
            var t = _blendTime > 0f ? (Time.time - _blendStart) / _blendTime : 1f;
            if (t >= 1f)
            {
                _blending = false;
                return;
            }

            var eased = 1f - Mathf.Pow(1f - t, 3f);
            position = Vector3.Lerp(_blendFromPosition, position, eased) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * _blendArc);
            rotation = Quaternion.Slerp(_blendFromRotation, rotation, eased);
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

            var point = spot.GetPinPoint(PinSlot);
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
