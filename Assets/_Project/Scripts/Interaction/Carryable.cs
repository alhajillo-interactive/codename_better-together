using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Player;
using BetterTogether.Match;
using Fusion;
using UnityEngine;

namespace BetterTogether.Interaction
{
    /// <summary>Why an item left a holder's hands. Sent with the release request and recorded by the playtest telemetry.</summary>
    public enum DropReason : byte
    {
        Voluntary,
        Kick,
        Distance,
        Respawn,
        Fall,
        Handoff,
    }

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

        [Header("Local prediction (presentation only)")]
        [Tooltip("Seconds a locally predicted grab, release or placement is shown before the authority must confirm it.")]
        [SerializeField, Min(0f)] private float _predictionTimeout = 0.5f;
        [Tooltip("Extra seconds added to twice the round-trip time when waiting for the authority to confirm; the wait is never shorter than the timeout above.")]
        [SerializeField, Min(0f)] private float _predictionRttMargin = 0.15f;
        [Tooltip("Seconds after a grab timed out during which a late confirmation still blends the item into the hand instead of snapping.")]
        [SerializeField, Min(0f)] private float _lateConfirmWindow = 1f;
        [Tooltip("A confirmed release keeps its predicted pose until the replicated pose is this close (metres), so the item does not jump back and fall again.")]
        [SerializeField, Min(0f)] private float _releaseMatchDistance = 0.05f;
        [Tooltip("Longest a confirmed release waits (seconds) for the replicated pose to catch up before blending to it.")]
        [SerializeField, Min(0f)] private float _releaseHoldCap = 0.15f;
        [Tooltip("Seconds the item takes to fly back to its replicated pose when a prediction is rejected or times out.")]
        [SerializeField, Min(0f)] private float _reconcileBlendTime = 0.15f;
        [Tooltip("Heard only by the player who grabs the item, the instant they press the interact key. May be empty.")]
        [SerializeField] private SoundCue _pickupSfx;
        [Tooltip("Degrees of FOV punch the local player feels when grabbing the item. 0 disables it.")]
        [SerializeField, Min(0f)] private float _pickupFovPunch = 1.5f;
        [Tooltip("Seconds the grab FOV punch lasts.")]
        [SerializeField, Min(0f)] private float _pickupFovPunchTime = 0.1f;

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
        private const float DropSeparationMargin = 0.02f;
        private const float DropRiseStep = 0.15f;
        private const float DropBoxScale = 0.9f;
        // Margin kept between a released item and the first obstacle on its way out (same role as the held-item skin).
        private const float HoldSkin = 0.04f;
        private static readonly Collider[] OverlapBuffer = new Collider[16];
        private static readonly RaycastHit[] SweepHits = new RaycastHit[16];

        private struct DropContext
        {
            public Vector3 Axis;
            public Vector3 Facing;
            public float CapsuleRadius;
            public LayerMask Mask;
        }

        private enum PoseKind : byte { None, Held, Pinned }

        // What the local peer is showing ahead of the authority. Never exists on the authority or on a proxy of the player.
        private enum PredictionKind : byte { None, Grab, Release, Place }

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
        private Vector3 _holdHalfExtents = new(0.15f, 0.15f, 0.15f);
        private Vector3 _holdCenter;
        private bool _smoothValid;
        private Vector3 _smoothOffset;
        private Quaternion _smoothRotation;
        private Vector3 _smoothHolderPosition;
        private float _swaySpeed;
        private float _swayPhase;
        private DropContext _lastDrop;
        private bool _wasSoloMoving;
        private bool _releaseHandled;

        // Local prediction (not networked). _predictedHolder is the local interactor that asked, for every kind.
        private PredictionKind _prediction;
        private PlayerInteractor _predictedHolder;
        private float _predictedUntil;
        private NetworkBehaviour _predictedSpot;
        private IPinSpot _predictedPinSpot;
        private int _predictedSlot;
        private Vector3 _predictedReleasePosition;
        private Quaternion _predictedReleaseRotation;
        private bool _reconcileBlend;
        private bool _releaseConfirmed;
        private float _releaseConfirmedAt;
        private float _lateConfirmUntil;

        [Networked] private PlayerInteractor HolderA { get; set; }
        [Networked] private PlayerInteractor HolderB { get; set; }
        [Networked] private NetworkBehaviourId PinOwnerId { get; set; }
        [Networked] private byte PinSlot { get; set; }

        public string DisplayName => _displayName;

        /// <summary>Half-extents of the item's colliders in its own (scaled) axes, in metres. Independent of how it is rotated in the scene.</summary>
        public Vector3 HoldHalfExtents => _holdHalfExtents;

        /// <summary>Centre of the item's colliders relative to its pivot, in its own (scaled) axes.</summary>
        public Vector3 HoldCenter => _holdCenter;

        /// <summary>Largest horizontal local half-extent, in metres. A held item is posed this much farther from the eye.</summary>
        public float HoldRadius => Mathf.Max(0.05f, _holdHalfExtents.x, _holdHalfExtents.z);

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

        /// <summary>
        /// HUD only: the item an interactor is holding as far as the local presentation shows. Includes a grab that is
        /// predicted but not confirmed yet, and leaves out an item that was just released or placed. Game logic must use
        /// <see cref="FindHeldBy"/>, which only reads the replicated state.
        /// </summary>
        public static Carryable FindHeldOrPredictedBy(PlayerInteractor interactor)
        {
            foreach (var carryable in All)
            {
                if (carryable._prediction == PredictionKind.Grab && carryable._predictedHolder == interactor)
                    return carryable;
                if (carryable.IsHeldBy(interactor) && carryable._prediction != PredictionKind.Release && carryable._prediction != PredictionKind.Place)
                    return carryable;
            }

            return null;
        }

        /// <summary>True while the local peer shows a grab, release or placement that the authority has not confirmed yet.</summary>
        public bool HasPrediction
        {
            get
            {
                UpdatePrediction();
                return _prediction != PredictionKind.None;
            }
        }

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _startPosition = transform.position;
            _startRotation = transform.rotation;
            _startPoseValid = ComputeStartPoseValid();
            ComputeHoldBox();
        }

        // The oriented box of the colliders in the item's local space (scaled), so it does not depend on how the item
        // is rotated in the scene. Colliders are still enabled in Awake.
        private void ComputeHoldBox()
        {
            var found = false;
            var bounds = default(Bounds);
            foreach (var c in _colliders)
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;

                GetLocalBox(c, out var center, out var extents);
                for (var i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    var corner = transform.InverseTransformPoint(c.transform.TransformPoint(center + Vector3.Scale(extents, sign)));
                    if (found)
                        bounds.Encapsulate(corner);
                    else
                        bounds = new Bounds(corner, Vector3.zero);
                    found = true;
                }
            }

            if (!found)
                return;

            var scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            _holdHalfExtents = Vector3.Scale(bounds.extents, scale);
            _holdCenter = Vector3.Scale(bounds.center, scale);
        }

        // A collider's box in its own transform space.
        private static void GetLocalBox(Collider collider, out Vector3 center, out Vector3 extents)
        {
            switch (collider)
            {
                case BoxCollider box:
                    center = box.center;
                    extents = box.size * 0.5f;
                    return;
                case SphereCollider sphere:
                    center = sphere.center;
                    extents = Vector3.one * sphere.radius;
                    return;
                case CapsuleCollider capsule:
                    center = capsule.center;
                    extents = Vector3.one * capsule.radius;
                    extents[capsule.direction] = Mathf.Max(capsule.height * 0.5f, capsule.radius);
                    return;
                case MeshCollider { sharedMesh: not null } meshCollider:
                    center = meshCollider.sharedMesh.bounds.center;
                    extents = meshCollider.sharedMesh.bounds.extents;
                    return;
                default:
                    // Unknown shape: the world bounds seen from the collider are the best guess.
                    center = collider.transform.InverseTransformPoint(collider.bounds.center);
                    extents = collider.bounds.extents;
                    return;
            }
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

            // A prediction belongs to a peer that is not the authority; the replicated state rules from now on.
            var wasPresenting = _prediction != PredictionKind.None || _reconcileBlend;
            ClearPrediction(!HasStateAuthority);
            if (HasStateAuthority && wasPresenting && HolderCount == 0 && !IsPinned)
            {
                // The new master must not drop the item from where a prediction or blend drew it (e.g. in front of the face):
                // it goes back to the last replicated pose first.
                _blending = false;
                _reconcileBlend = false;
                if (_hasRested)
                    transform.SetPositionAndRotation(_restPosition, _restRotation);
            }

            ApplyPhysicsOwnership();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            ClearPrediction(false);
            _blending = false;
            _reconcileBlend = false;
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
            // Spots can forbid taking their items directly (hiding spots give them back only when searched).
            if (IsPinned && Runner.TryFindBehaviour(PinOwnerId, out NetworkBehaviour owner)
                && (owner is FireplaceManager || owner is IPinSpot { AllowsTakeFromPin: false }))
                return false;

            return HolderCount == 0
                || (HeldByGnomes && interactor.Role == PlayerRole.Gnome && HolderCount < _gnomesRequired);
        }

        public void Interact(PlayerInteractor interactor)
        {
            // A second press while the first grab is still in flight changes nothing.
            UpdatePrediction();
            if (_prediction == PredictionKind.Grab)
                return;

            interactor.NoteGrabRequest();
            RPC_RequestGrab(interactor.Id);
            PredictGrab(interactor);
        }

        // Local feedback for the player who pressed the key, then (if the authority is far away) the predicted pose.
        // The authority's decisions are not predicted: only a lone grab of an item that needs one holder, and only when
        // nobody holds it in the replicated state. A two-gnome item stays on the ground until the authority says otherwise.
        private void PredictGrab(PlayerInteractor who)
        {
            if (who != PlayerInteractor.Local)
                return;

            _pickupSfx.Play();
            if (_pickupFovPunch > 0f && CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(_pickupFovPunch, _pickupFovPunchTime);

            if (HasStateAuthority || Inert || _prediction != PredictionKind.None || HolderCount != 0 || RequiredHolders(who) != 1)
                return;

            // A reconcile blend that fails right away starts from where the item was last seen.
            _drawnPosition = transform.position;
            _drawnRotation = transform.rotation;
            _prediction = PredictionKind.Grab;
            _predictedHolder = who;
            _predictedUntil = Time.time + CurrentPredictionTimeout();
        }

        /// <summary>
        /// Local player only, before asking the authority to rest this item on <paramref name="spot"/>: starts flying it
        /// to the slot it will most likely take. Presentation only; the replicated pin decides the final slot.
        /// </summary>
        public void PredictPin<T>(T spot, int slot = 0) where T : NetworkBehaviour, IPinSpot
        {
            UpdatePrediction();
            var who = PlayerInteractor.Local;
            if (!_isSpawned || HasStateAuthority || Inert || _prediction != PredictionKind.None || spot == null || !IsHeldBy(who) || HolderCount != 1 || !IsMoving)
                return;

            _drawnPosition = transform.position;
            _drawnRotation = transform.rotation;
            _prediction = PredictionKind.Place;
            _predictedHolder = who;
            _predictedSpot = spot;
            _predictedPinSpot = spot;
            _predictedSlot = slot;
            _predictedUntil = Time.time + CurrentPredictionTimeout();
        }

        public void RequestRelease(PlayerInteractor who)
        {
            // A second press while the first release or placement is still in flight changes nothing.
            UpdatePrediction();
            if (_prediction == PredictionKind.Release || _prediction == PredictionKind.Place)
                return;

            PredictRelease(who);
            RPC_RequestRelease(who.Id, false, default, DropReason.Voluntary);
        }

        // The item stops following the view at once. Until the replicated state says it is free, it is drawn where the
        // authority will put it: the same safe release pose the authority computes (so nothing is drawn inside a wall or the
        // holder), instead of hanging in front of the view or sitting at an arbitrary old pose. Physics, being the master's,
        // starts from that pose and arrives with the replicated state.
        private void PredictRelease(PlayerInteractor who)
        {
            if (!_isSpawned || HasStateAuthority || Inert || who != PlayerInteractor.Local || !IsHeldBy(who) || !IsMoving)
                return;

            var position = transform.position;
            var rotation = transform.rotation;
            if (HolderCount == 1 && who.TryGetHoldPose(this, true, out var holdPosition, out var holdRotation))
            {
                position = SettleSafely(MakeDropContext(who), holdPosition, holdRotation);
                rotation = holdRotation;
            }

            _prediction = PredictionKind.Release;
            _predictedHolder = who;
            _predictedReleasePosition = position;
            _predictedReleaseRotation = rotation;
            _releaseConfirmed = false;
            _predictedUntil = Time.time + CurrentPredictionTimeout();
        }

        /// <summary>Releases the item for a reason other than the holder's own choice (e.g. the holder is about to respawn).</summary>
        public void RequestRelease(PlayerInteractor who, DropReason reason) => RPC_RequestRelease(who.Id, false, default, reason);

        /// <summary>Releases the item and moves it to <paramref name="dropPoint"/> first (e.g. knocked out of a kicked gnome's hands).</summary>
        public void RequestRelease(PlayerInteractor who, Vector3 dropPoint, DropReason reason = DropReason.Kick) =>
            RPC_RequestRelease(who.Id, true, dropPoint, reason);

        /// <summary>Authority only: makes every holder let go.</summary>
        public void ForceReleaseAll()
        {
            HolderA = null;
            HolderB = null;
            // The caller places the item itself (pin, eat, reset): the lost-holder settle must not move it again.
            _releaseHandled = true;
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

            if (Telemetry.Enabled && announce) LogDropAll(DropReason.Respawn);
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

            if (Telemetry.Enabled) Telemetry.Begin("drop", from).Str("object", _displayName).Int("holders", 0).Str("reason", ReasonName(DropReason.Handoff)).Send();
            if (Telemetry.Enabled) Telemetry.Begin("pickup", to).Str("object", _displayName).Int("holders", 1).Send();
            HolderA = to;
            HolderB = null;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestGrab(NetworkBehaviourId interactorId)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !CanInteract(who))
                return;

            // Taking what the camper held last is a steal (the flag clears once a gnome holds it, so it can't be farmed).
            if (LastHeldByCamper && who.Role == PlayerRole.Gnome && MatchManager.Instance != null)
            {
                MatchManager.Instance.Award(who.Object.StateAuthority, MatchManager.ScoreReason.Steal);
                LastHeldByCamper = false;
            }

            Unpin();
            Compact();
            if (Valid(HolderA) == null)
                HolderA = who;
            else
                HolderB = who;

            if (Telemetry.Enabled) Telemetry.Begin("pickup", who).Str("object", _displayName).Int("holders", HolderCount).Send();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestRelease(NetworkBehaviourId interactorId, NetworkBool hasDropPoint, Vector3 dropPoint, DropReason reason)
        {
            if (!Runner.TryFindBehaviour(interactorId, out PlayerInteractor who) || !IsHeldBy(who))
                return;

            _releaseHandled = true;

            // Let go at a coherent, empty place: in front of the holder, outside its capsule and out of geometry.
            if (HolderCount == 1 && IsMoving)
                PlaceForRelease(who, hasDropPoint, dropPoint);
            else if (hasDropPoint && IsMoving)
                transform.position = new Vector3(dropPoint.x, transform.position.y, dropPoint.z);

            if (HolderA == who)
                HolderA = null;
            else
                HolderB = null;

            Compact();
            if (Telemetry.Enabled) Telemetry.Begin("drop", who).Str("object", _displayName).Int("holders", HolderCount).Str("reason", ReasonName(reason)).Send();
        }

        // Telemetry only. Authority: one drop row per current holder, before they let go.
        private void LogDropAll(DropReason reason)
        {
            var count = HolderCount;
            for (var i = 0; i < count; i++)
                Telemetry.Begin("drop", Holder(i)).Str("object", _displayName).Int("holders", 0).Str("reason", ReasonName(reason)).Send();
        }

        private static string ReasonName(DropReason reason) => reason switch
        {
            DropReason.Kick => "kick",
            DropReason.Distance => "distance",
            DropReason.Respawn => "respawn",
            DropReason.Fall => "fall",
            DropReason.Handoff => "handoff",
            _ => "voluntary",
        };

        // Authority only. The release pose is computed explicitly (the hold pose with its retraction, no local smoothing),
        // moved to the drop point if given, then settled by a sweep outside the holder's capsule and out of any geometry.
        private void PlaceForRelease(PlayerInteractor holder, bool hasDropPoint, Vector3 dropPoint)
        {
            if (!holder.TryGetHoldPose(this, true, out var position, out var rotation))
            {
                position = transform.position;
                rotation = transform.rotation;
            }

            if (hasDropPoint)
                position = new Vector3(dropPoint.x, position.y, dropPoint.z);

            position = SettleSafely(MakeDropContext(holder), position, rotation);
            transform.SetPositionAndRotation(position, rotation);
        }

        // Footprint of a holder that is needed to place an item next to it. Kept apart from the holder itself so it can be
        // remembered and still used after the holder has left (see SettleAfterLostHolder).
        private static DropContext MakeDropContext(PlayerInteractor holder)
        {
            var context = new DropContext { Axis = holder.transform.position, Mask = holder.HoldObstacleMask };
            if (holder.TryGetComponent<CharacterController>(out var capsule))
            {
                var scale = capsule.transform.lossyScale;
                context.CapsuleRadius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                context.Axis = capsule.transform.TransformPoint(capsule.center);
            }

            var facing = holder.transform.forward;
            facing.y = 0f;
            context.Facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
            return context;
        }

        // Projection of the item's oriented box (half-extents rotated by rotation) on a horizontal direction.
        private float ExtentAlong(Quaternion rotation, Vector3 direction) =>
            Mathf.Abs(Vector3.Dot(rotation * Vector3.right, direction)) * _holdHalfExtents.x
            + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, direction)) * _holdHalfExtents.y
            + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, direction)) * _holdHalfExtents.z;

        // Distance from the holder's axis to the item's pivot, along direction, at which the box clears the capsule.
        private float NeededDistance(in DropContext context, Quaternion rotation, Vector3 direction) =>
            Mathf.Max(0f, context.CapsuleRadius + ExtentAlong(rotation, direction) + DropSeparationMargin
                - Vector3.Dot(rotation * _holdCenter, direction));

        // Never teleports through geometry: the item is swept from the holder's axis toward the wanted spot and stops short of
        // the first obstacle. It goes in front (or where it was asked to go) if it fits, else behind, else to the sides; the
        // first spot whose box is clear wins. If none fits it tries rising above the best one, and as a last resort stays on
        // the holder's side at the safest swept distance (never above the head).
        private Vector3 SettleSafely(in DropContext context, Vector3 position, Quaternion rotation)
        {
            var flat = position - context.Axis;
            flat.y = 0f;
            var facing = context.Facing;
            var firstNeeded = NeededDistance(context, rotation, facing);
            var firstDirection = facing;
            var firstDesired = firstNeeded;
            if (flat.sqrMagnitude > 1e-4f)
            {
                var direction = flat.normalized;
                var needed = NeededDistance(context, rotation, direction);
                if (flat.magnitude >= needed)
                {
                    firstDirection = direction;
                    firstNeeded = needed;
                    firstDesired = flat.magnitude;
                }
            }

            var right = new Vector3(facing.z, 0f, -facing.x);
            var bestStop = -1f;
            var bestPosition = position;
            var bestDirection = firstDirection;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var direction = attempt switch { 0 => firstDirection, 1 => -facing, 2 => right, _ => -right };
                var needed = attempt == 0 ? firstNeeded : NeededDistance(context, rotation, direction);
                var desired = attempt == 0 ? firstDesired : needed;
                var stop = SweepDistance(context, position.y, rotation, direction, desired);
                var candidate = new Vector3(context.Axis.x + direction.x * stop, position.y, context.Axis.z + direction.z * stop);
                if (stop > bestStop)
                {
                    bestStop = stop;
                    bestPosition = candidate;
                    bestDirection = direction;
                }

                if (stop >= needed - 1e-3f && !BoxBlocked(candidate, rotation, context.Mask))
                    return candidate;
            }

            for (var i = 1; i <= DepenetrationSteps; i++)
            {
                var candidate = bestPosition + Vector3.up * (i * DropRiseStep);
                if (!BoxBlocked(candidate, rotation, context.Mask))
                    return candidate;
            }

            return bestPosition;
        }

        // How far (pivot distance from the holder's axis, at most desired) the item can travel along a horizontal direction.
        // A box cast finds solid obstacles; a ray from the box centre also catches walls thinner than the box that the cast
        // starts inside of (a cast never reports a collider it begins overlapping).
        private float SweepDistance(in DropContext context, float height, Quaternion rotation, Vector3 direction, float desired)
        {
            var center = new Vector3(context.Axis.x, height, context.Axis.z) + rotation * _holdCenter;
            var extent = ExtentAlong(rotation, direction);
            var limit = desired;

            var count = Physics.BoxCastNonAlloc(center, _holdHalfExtents * DropBoxScale, direction, SweepHits, rotation,
                desired + HoldSkin, context.Mask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (SweepHits[i].distance > 0f && !IgnoresForSweep(SweepHits[i].collider))
                    limit = Mathf.Min(limit, SweepHits[i].distance - HoldSkin);
            }

            count = Physics.RaycastNonAlloc(center, direction, SweepHits, desired + extent + HoldSkin, context.Mask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!IgnoresForSweep(SweepHits[i].collider))
                    limit = Mathf.Min(limit, SweepHits[i].distance - extent - HoldSkin);
            }

            return Mathf.Max(0f, limit);
        }

        // Same filter as a held item's (PlayerInteractor.IgnoresForHold), usable once the holder is gone: this item and players.
        private bool IgnoresForSweep(Collider collider) =>
            collider.transform.IsChildOf(transform) || collider.GetComponentInParent<PlayerInteractor>() != null;

        // Everything counts, the holder's own capsule included; only this item's colliders are skipped.
        private bool BoxBlocked(Vector3 position, Quaternion rotation, LayerMask mask)
        {
            var count = Physics.OverlapBoxNonAlloc(position + rotation * _holdCenter, _holdHalfExtents * DropBoxScale, OverlapBuffer, rotation,
                mask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!OverlapBuffer[i].transform.IsChildOf(transform))
                    return true;
            }

            return false;
        }

        // Authority only. The holder left without a release call (disconnected): the item is still in its last carried pose,
        // which may be inside a wall or the holder's capsule, so it gets the same safe placement before physics takes over.
        private void SettleAfterLostHolder()
        {
            transform.position = SettleSafely(_lastDrop, transform.position, transform.rotation);
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
                if (Telemetry.Enabled) LogDropAll(DropReason.Distance);
                HolderA = null;
                HolderB = null;
            }
            else if (HolderCount == 1 && !IsMoving && DistanceToHolder(Holder(0)) > _maxSoloGrabDistance)
            {
                if (Telemetry.Enabled) LogDropAll(DropReason.Distance);
                HolderA = null;
            }

            // A lone carrier vanished without a release call (it disconnected): settle the item like a normal release.
            if (_wasSoloMoving && HolderCount == 0 && !_releaseHandled && !IsPinned && !Inert)
                SettleAfterLostHolder();

            _releaseHandled = false;

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

            // Raw hold pose: no local smoothing or bob (those are drawing only). Release recomputes it with retraction.
            if (TryGetFollowPose(false, out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);

            // Remember the lone carrier's footprint for the tick it might disappear in.
            _wasSoloMoving = HolderCount == 1 && IsMoving;
            if (_wasSoloMoving)
                _lastDrop = MakeDropContext(Holder(0));
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
            // A predicted grab also keeps them off: the item sits in front of the view before the replicated state says so.
            var shouldEnable = !IsMoving && !Inert && !_blending && _prediction != PredictionKind.Grab;
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

            UpdatePrediction();
            if (_prediction == PredictionKind.Release)
            {
                DrawPredictedRelease();
                return;
            }

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
            else if (kind == PoseKind.Held && _lastKind == PoseKind.None && Time.time < _lateConfirmUntil)
                StartBlend(false, _drawnPosition, _drawnRotation);
            if (kind == PoseKind.Held)
                _lateConfirmUntil = 0f;

            _lastKind = kind;
            _lastKeyId = keyId;
            _lastKeySlot = keySlot;

            if (!hasPose)
            {
                _smoothValid = false;
                _hasRested = true;
                _restPosition = transform.position;
                _restRotation = transform.rotation;

                // A rejected prediction flies back to the replicated pose NetworkTransform just drew (a loose item has no
                // pose of its own); any other blend ends here.
                if (_blending && _reconcileBlend)
                {
                    position = _restPosition;
                    rotation = _restRotation;
                    BlendPose(ref position, ref rotation);
                    transform.SetPositionAndRotation(position, rotation);
                    _drawnPosition = position;
                    _drawnRotation = rotation;
                    return;
                }

                _blending = false;
                _reconcileBlend = false;
                return;
            }

            // Weight and walking bob only for what the local player holds alone; proxies already interpolate.
            var soloHolder = SoloPoseHolder;
            if (kind == PoseKind.Held && soloHolder != null && soloHolder == PlayerInteractor.Local)
                ApplyHoldWeight(soloHolder, ref position, ref rotation);
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
                _swayPhase = Mathf.Repeat(_swayPhase + _swaySpeed * dt * SwayRadiansPerMeter, 4f * Mathf.PI);
            }

            var amount = Mathf.Clamp01(_swaySpeed / SwayFullSpeed) * holder.SwayAmplitude;
            var sway = Vector3.up * (Mathf.Sin(_swayPhase) * amount)
                       + holder.transform.right * (Mathf.Cos(_swayPhase * 0.5f) * amount * 0.5f);
            position = eye + _smoothOffset + sway;
            rotation = _smoothRotation;
        }

        // The holder whose view poses the item while it is held alone: the predicted one until the state confirms it.
        private PlayerInteractor SoloPoseHolder =>
            _prediction == PredictionKind.Grab ? _predictedHolder : HolderCount == 1 ? Holder(0) : null;

        private PoseKind GetPoseKey(out NetworkBehaviourId id, out int slot)
        {
            if (_prediction == PredictionKind.Grab)
            {
                id = _predictedHolder.Id;
                slot = 0;
                return PoseKind.Held;
            }

            if (_prediction == PredictionKind.Place)
            {
                id = _predictedSpot.Id;
                slot = _predictedSlot;
                return PoseKind.Pinned;
            }

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
            _blendTime = _reconcileBlend ? _reconcileBlendTime : toPin ? _snapBlendTime : _handOffBlendTime;
            _blendArc = _reconcileBlend ? 0f : toPin ? _snapArc : _handOffArc;
        }

        // Presentation only. Compares the prediction with the replicated state and ends it: confirmed (no jump, the poses
        // match), rejected (someone else holds it, immediate) or timed out (the blend flies the item back to the replicated
        // pose). Also called by the request guards, so a confirmation that just arrived never swallows a key press.
        private void UpdatePrediction()
        {
            if (_prediction == PredictionKind.None)
                return;

            var who = _predictedHolder;
            if (HasStateAuthority || Inert || who == null || who != PlayerInteractor.Local || Valid(who) == null)
            {
                ClearPrediction(false);
                return;
            }

            if (_prediction == PredictionKind.Place && (_predictedSpot == null || _predictedSpot.Object == null || !_predictedSpot.Object.IsValid))
            {
                ClearPrediction(true);
                return;
            }

            // A confirmed release keeps its predicted pose until the replicated pose has caught up, so the item does not
            // jump back to the hand and fall again; then it blends to wherever the replicated pose is.
            if (_prediction == PredictionKind.Release && !IsHeldBy(who))
            {
                if (!_releaseConfirmed)
                {
                    _releaseConfirmed = true;
                    _releaseConfirmedAt = Time.time;
                }

                var gap = (transform.position - _predictedReleasePosition).sqrMagnitude;
                if (gap <= _releaseMatchDistance * _releaseMatchDistance || Time.time - _releaseConfirmedAt >= _releaseHoldCap)
                    ClearPrediction(true);
                return;
            }

            var confirmed = false;
            var rejected = false;
            switch (_prediction)
            {
                case PredictionKind.Grab:
                    confirmed = IsHeldBy(who);
                    rejected = !confirmed && HolderCount > 0;
                    break;
                case PredictionKind.Place:
                    // Any slot confirms it: if the authority picked another one, the pose key changes and the item blends there.
                    confirmed = IsPinned;
                    rejected = !confirmed && !IsHeldBy(who);
                    break;
            }

            if (confirmed)
            {
                ClearPrediction(false);
            }
            else if (rejected || Time.time >= _predictedUntil)
            {
                // A grab that merely timed out may still be confirmed later (very high ping): that arrival blends in.
                if (!rejected && _prediction == PredictionKind.Grab)
                    _lateConfirmUntil = Time.time + _lateConfirmWindow;
                ClearPrediction(true);
            }
        }

        // Timeout of a prediction: never shorter than the serialized one, and long enough for a round trip through the
        // relay and the master plus the tick that carries the answer.
        private float CurrentPredictionTimeout()
        {
            var rtt = Runner != null ? (float)Runner.GetPlayerRtt(Runner.LocalPlayer) : 0f;
            return Mathf.Max(_predictionTimeout, 2f * rtt + _predictionRttMargin);
        }

        /// <summary>Whether this item is flying to <paramref name="spot"/> in the local presentation, and which slot (HUD ghosts).</summary>
        public bool TryGetPredictedPin(NetworkBehaviour spot, out int slot)
        {
            var predicted = _prediction == PredictionKind.Place && _predictedSpot == spot;
            slot = predicted ? _predictedSlot : -1;
            return predicted;
        }

        // Ends the local prediction. With reconcile, the item flies from where it is drawn back to its replicated pose.
        private void ClearPrediction(bool reconcile)
        {
            if (_prediction == PredictionKind.None)
                return;

            _prediction = PredictionKind.None;
            _releaseConfirmed = false;
            _predictedHolder = null;
            _predictedSpot = null;
            _predictedPinSpot = null;
            if (!reconcile)
                return;

            _blending = true;
            _reconcileBlend = true;
            _blendFromPosition = _drawnPosition;
            _blendFromRotation = _drawnRotation;
            _blendStart = Time.time;
            _blendTime = _reconcileBlendTime;
            _blendArc = 0f;
        }

        // A released item is drawn at the pose the authority will give it, with no hold weight and no blend.
        private void DrawPredictedRelease()
        {
            _smoothValid = false;
            _blending = false;
            _reconcileBlend = false;
            _lastKind = PoseKind.None;
            transform.SetPositionAndRotation(_predictedReleasePosition, _predictedReleaseRotation);
            _drawnPosition = _predictedReleasePosition;
            _drawnRotation = _predictedReleaseRotation;
            _hasRested = true;
            _restPosition = _predictedReleasePosition;
            _restRotation = _predictedReleaseRotation;
        }

        // The pose a predicted grab (the holder's view) or placement (the slot) shows ahead of the replicated state.
        private bool TryGetPredictedPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = transform.rotation;
            switch (_prediction)
            {
                case PredictionKind.Grab:
                    return _predictedHolder.TryGetHoldPose(this, true, out position, out rotation);
                case PredictionKind.Place:
                    var point = _predictedPinSpot.GetPinPoint(_predictedSlot);
                    if (point == null)
                        return false;
                    position = point.position;
                    rotation = point.rotation;
                    return true;
                default:
                    return false;
            }
        }

        // Ease-out cubic along the way, with an arc on top; the real pose is always the destination.
        private void BlendPose(ref Vector3 position, ref Quaternion rotation)
        {
            var t = _blendTime > 0f ? (Time.time - _blendStart) / _blendTime : 1f;
            if (t >= 1f)
            {
                _blending = false;
                _reconcileBlend = false;
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
            if (presentation && TryGetPredictedPose(out position, out rotation))
                return true;

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

        /// <summary>The point of the pin spot the item currently rests on, if it is pinned (and not inert).</summary>
        public bool TryGetPinPoint(out Transform point)
        {
            point = null;
            if (Inert || !IsPinned || Runner == null || !Runner.TryFindBehaviour(PinOwnerId, out NetworkBehaviour owner) || owner is not IPinSpot spot)
                return false;

            point = spot.GetPinPoint(PinSlot);
            return point != null;
        }

        private bool TryGetPinPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = transform.rotation;
            if (!TryGetPinPoint(out var point))
                return false;

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
