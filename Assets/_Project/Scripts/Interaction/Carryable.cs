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
    /// and every client poses the item on its holders' carry anchors.
    /// Physics model: the state authority simulates; proxies are kinematic and follow NetworkTransform. The authority
    /// keeps the item kinematic while it is carried, pinned or inert, and lets physics drop it otherwise.
    /// An item lost below the level (or left outside <see cref="LevelBounds"/> for too long) returns to its start pose.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Carryable : NetworkBehaviour, IInteractable, IMatchResettable, IStateAuthorityChanged
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

        [Header("Holding")]
        [Tooltip("Rotation of the item relative to the holder's view while it is held alone, e.g. a log lying across the view.")]
        [SerializeField] private Vector3 _holdEuler;

        [Header("Lost items")]
        [Tooltip("Below this world Y the item is lost and returns to its start pose.")]
        [SerializeField] private float _killHeight = -5f;
        [Tooltip("Seconds the item may rest outside the level bounds before it returns to its start pose. 0 disables this check.")]
        [SerializeField, Min(0f)] private float _lostSeconds = 20f;
        [Tooltip("Heard by everyone, from the start pose, when a lost item returns.")]
        [SerializeField] private SoundCue _respawnSfx;

        private const float ReceiveFovPunch = 2f;
        private const float ReceiveFovPunchTime = 0.12f;

        // Walking bob of an item held by the local player: radians of phase per metre walked, and the speed (m/s) at full bob.
        private const float SwayRadiansPerMeter = 9f;
        private const float SwayFullSpeed = 4f;
        private const int DepenetrationSteps = 8;
        private static readonly Collider[] OverlapBuffer = new Collider[8];

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
        private float _outsideSince = -1f;
        private bool _startPoseValid;
        private bool _startPoseRechecked;
        private bool _startPoseWarned;
        private float _holdRadius = 0.15f;
        private bool _smoothValid;
        private Vector3 _smoothOffset;
        private Quaternion _smoothRotation;
        private Vector3 _smoothHolderPosition;
        private float _swaySpeed;
        private float _swayPhase;

        [Networked] private PlayerInteractor HolderA { get; set; }
        [Networked] private PlayerInteractor HolderB { get; set; }
        [Networked] private NetworkBehaviourId PinOwnerId { get; set; }
        [Networked] private byte PinSlot { get; set; }

        public string DisplayName => _displayName;

        /// <summary>Largest horizontal half-extent of the item's colliders, in metres. A held item is posed this much farther from the eye.</summary>
        public float HoldRadius => _holdRadius;

        /// <summary>Rotation of the item relative to the holder's view while held alone.</summary>
        public Vector3 HoldEuler => _holdEuler;

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
            _startPoseValid = ComputeStartPoseValid();
            _holdRadius = ComputeHoldRadius();
        }

        // Colliders are still enabled in Awake, so their combined bounds are the item's real size.
        private float ComputeHoldRadius()
        {
            var found = false;
            var bounds = default(Bounds);
            foreach (var c in _colliders)
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;

                if (found)
                    bounds.Encapsulate(c.bounds);
                else
                    bounds = c.bounds;
                found = true;
            }

            return found ? Mathf.Max(0.05f, bounds.extents.x, bounds.extents.z) : 0.15f;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            All.Add(this);
            _outsideSince = -1f;
            ApplyPhysicsOwnership();
        }

        /// <summary>Called on every peer when the state authority of this item changes (e.g. the master leaves).</summary>
        public void StateAuthorityChanged()
        {
            if (!_isSpawned)
                return;

            // The loss timers only run on the authority; a new authority must not inherit a stale countdown.
            _freeSince = -1f;
            _outsideSince = -1f;
            ApplyPhysicsOwnership();
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

        /// <summary>Authority only: drops the item and puts it back where it started, without a sound.</summary>
        public void ResetForMatch() => ReturnToStart(false);

        /// <summary>Authority only: drops the item and puts it back at its start pose. Everyone hears it when <paramref name="announce"/> is true.</summary>
        private void ReturnToStart(bool announce)
        {
            if (!HasStateAuthority)
                return;

            WasHandled = false;
            LastHeldByCamper = false;
            ForceReleaseAll();
            Unpin();
            _freeSince = -1f;
            _outsideSince = -1f;

            // The one kinematic rule decides: an inert item stays kinematic, a loose one goes back to physics at rest.
            ApplyAuthorityKinematic();
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            transform.SetPositionAndRotation(_startPosition, _startRotation);
            if (_networkTransform != null)
                _networkTransform.Teleport(_startPosition, _startRotation);

            if (announce)
                RPC_PlayRespawn(_startPosition);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayRespawn(Vector3 position) => _respawnSfx.PlayAt(position);

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

            // Never let go of an item inside a wall or under the floor: bring it back toward the holder's eyes first.
            if (HolderCount == 1 && IsMoving)
                DepenetrateFrom(who);

            if (HolderA == who)
                HolderA = null;
            else
                HolderB = null;

            Compact();
        }

        // Authority only. Moves the item along the line to the holder's eyes until it no longer overlaps geometry.
        private void DepenetrateFrom(PlayerInteractor holder)
        {
            var eye = holder.EyePosition;
            var start = transform.position;
            var radius = _holdRadius * 0.5f;
            if (!OverlapsGeometry(start, radius, holder))
                return;

            for (var i = 1; i < DepenetrationSteps; i++)
            {
                var candidate = Vector3.Lerp(start, eye, i / (float)DepenetrationSteps);
                if (OverlapsGeometry(candidate, radius, holder))
                    continue;

                transform.position = candidate;
                return;
            }

            transform.position = eye;
        }

        private bool OverlapsGeometry(Vector3 center, float radius, PlayerInteractor holder)
        {
            var count = Physics.OverlapSphereNonAlloc(center, radius, OverlapBuffer, holder.HoldObstacleMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!holder.IgnoresForHold(OverlapBuffer[i], this))
                    return true;
            }

            return false;
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

            // Lost items return to their start pose. This runs before the kinematic rule, which then sees the released item.
            CheckLost(HolderCount == 0 && !IsPinned && !Inert);

            ApplyAuthorityKinematic();

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

            // The simulation pose has no smoothing and no retraction, so a release starts from a coherent place.
            if (TryGetFollowPose(false, out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Proxies are kinematic and follow NetworkTransform; the authority applies the shared kinematic rule.
        /// Called from Spawned and StateAuthorityChanged, so every peer agrees after a spawn or a master change.
        /// </summary>
        private void ApplyPhysicsOwnership()
        {
            // Rigidbody interpolation stays off: the replicated pose is presented in Render.
            _body.interpolation = RigidbodyInterpolation.None;
            if (!HasStateAuthority)
            {
                _body.isKinematic = true;
                return;
            }

            ApplyAuthorityKinematic();
        }

        // Authority only. The one place that decides kinematic on the authority: carried, pinned or inert items are
        // kinematic and follow their holders or slot; loose items fall under gravity.
        private void ApplyAuthorityKinematic()
        {
            var shouldBeKinematic = IsMoving || IsPinned || Inert;
            if (_body.isKinematic == shouldBeKinematic)
                return;

            _body.isKinematic = shouldBeKinematic;
            if (!shouldBeKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
        }

        // Authority only. A loose item below the kill height returns at once; one outside LevelBounds returns after _lostSeconds.
        // Allocation-free: the countdown is a single float.
        private void CheckLost(bool loose)
        {
            if (!loose)
            {
                _outsideSince = -1f;
                return;
            }

            if (transform.position.y < _killHeight)
            {
                if (StartPoseIsUsable())
                    ReturnToStart(true);
                return;
            }

            if (_lostSeconds <= 0f || LevelBounds.Instance == null || LevelBounds.Instance.Contains(transform.position))
            {
                _outsideSince = -1f;
                return;
            }

            if (_outsideSince < 0f)
            {
                _outsideSince = Time.time;
                return;
            }

            if (Time.time - _outsideSince > _lostSeconds && StartPoseIsUsable())
                ReturnToStart(true);
        }

        // The start pose must itself be usable, or a returned item would be lost again. Rechecked once, the first time an
        // item is found lost, because LevelBounds may appear after Awake. Warns once and then stays silent.
        private bool StartPoseIsUsable()
        {
            if (!_startPoseRechecked)
            {
                _startPoseRechecked = true;
                _startPoseValid = ComputeStartPoseValid();
            }

            if (!_startPoseValid && !_startPoseWarned)
            {
                _startPoseWarned = true;
                var reason = _startPosition.y < _killHeight ? "below the kill height" : "outside LevelBounds";
                Debug.LogWarning($"{name}: start pose {_startPosition} is {reason}; lost items will not return to it.", this);
            }

            return _startPoseValid;
        }

        private bool ComputeStartPoseValid() =>
            _startPosition.y >= _killHeight && (LevelBounds.Instance == null || LevelBounds.Instance.Contains(_startPosition));

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

            var hasPose = TryGetFollowPose(true, out var position, out var rotation);
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
                _smoothValid = false;
                _blending = false;
                _hasRested = true;
                _restPosition = transform.position;
                _restRotation = transform.rotation;
                return;
            }

            // Weight and walking bob only for what the local player holds alone; proxies already interpolate.
            if (kind == PoseKind.Held && HolderCount == 1 && Holder(0) == PlayerInteractor.Local)
                ApplyHoldWeight(Holder(0), ref position, ref rotation);
            else
                _smoothValid = false;

            if (_blending)
                BlendPose(ref position, ref rotation);

            transform.SetPositionAndRotation(position, rotation);
            _drawnPosition = position;
            _drawnRotation = rotation;
        }

        // Local presentation: the item trails the view a little (smoothed relative to the eyes, so walking adds no lag)
        // and bobs with the player's steps. The first frame starts exactly on the target pose.
        private void ApplyHoldWeight(PlayerInteractor holder, ref Vector3 position, ref Quaternion rotation)
        {
            var dt = Time.deltaTime;
            var eye = holder.EyePosition;
            var offset = position - eye;
            var holderPosition = holder.transform.position;

            if (!_smoothValid)
            {
                _smoothValid = true;
                _smoothOffset = offset;
                _smoothRotation = rotation;
                _smoothHolderPosition = holderPosition;
                _swaySpeed = 0f;
            }
            else if (dt > 0f)
            {
                var follow = 1f - Mathf.Exp(-holder.HoldSharpness * dt);
                _smoothOffset = Vector3.Lerp(_smoothOffset, offset, follow);
                _smoothRotation = Quaternion.Slerp(_smoothRotation, rotation, follow);

                var moved = holderPosition - _smoothHolderPosition;
                moved.y = 0f;
                _smoothHolderPosition = holderPosition;
                var speed = Mathf.Min(moved.magnitude / dt, SwayFullSpeed * 2f);
                _swaySpeed = Mathf.Lerp(_swaySpeed, speed, follow);
                _swayPhase += _swaySpeed * dt * SwayRadiansPerMeter;
            }

            var amount = Mathf.Clamp01(_swaySpeed / SwayFullSpeed) * holder.SwayAmplitude;
            var sway = Vector3.up * (Mathf.Sin(_swayPhase) * amount)
                       + holder.transform.right * (Mathf.Cos(_swayPhase * 0.5f) * amount * 0.5f);
            position = eye + _smoothOffset + sway;
            rotation = _smoothRotation;
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

        // presentation: also retract a held item from geometry (every peer's drawn pose); the authority's simulation pose does not.
        private bool TryGetFollowPose(bool presentation, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = transform.rotation;
            if (!IsMoving)
                return TryGetPinPose(out position, out rotation);

            // A lone holder keeps the item in front of the view; two gnomes carry it between their anchors.
            if (HolderCount == 1 && Holder(0).TryGetHoldPose(this, presentation, out position, out rotation))
                return true;

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
