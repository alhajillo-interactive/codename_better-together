using BetterTogether.Diagnostics;
using BetterTogether.Fireplace;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Interaction
{
    /// <summary>
    /// Lets the local player focus an <see cref="IInteractable"/> by looking at it and use it with the interact key.
    /// What the player is holding is derived from the <see cref="Carryable"/>s themselves.
    /// </summary>
    public class PlayerInteractor : NetworkBehaviour
    {
        [Tooltip("Where carried objects are held, as a direct child of the player root.")]
        [SerializeField] private Transform _carryAnchor;
        [SerializeField] private float _reach = 2f;
        [SerializeField] private float _aimRadius = 0.1f;
        [SerializeField] private LayerMask _interactionMask = ~0;

        [Header("Holding")]
        [Tooltip("Camera anchor; held items are posed in front of it. If empty, a child named CameraAnchor is used, otherwise the player root.")]
        [SerializeField] private Transform _eye;
        [Tooltip("Distance of a held item in front of the eye, as a fraction of the eye height (the item radius is added).")]
        [SerializeField, Min(0f)] private float _holdDistanceFactor = 0.45f;
        [Tooltip("How far below the eye a held item sits, as a fraction of the eye height, so it does not cover the aim.")]
        [SerializeField, Min(0f)] private float _holdDropFactor = 0.18f;
        [Tooltip("A held item is never pulled closer to the eye than this (metres) when something is in the way.")]
        [SerializeField, Min(0f)] private float _minHoldDistance = 0.12f;
        [Tooltip("Lowest view pitch (degrees, negative = up) the held item follows.")]
        [SerializeField] private float _minHoldPitch = -45f;
        [Tooltip("Highest view pitch (degrees, positive = down) the held item follows.")]
        [SerializeField] private float _maxHoldPitch = 55f;
        [Tooltip("Layers a held item retracts from. The own player, the own item and other players are always ignored.")]
        [SerializeField] private LayerMask _holdObstacleMask = ~0;
        [Tooltip("Local presentation only: how fast the held item catches up with the view (higher = stiffer).")]
        [SerializeField, Min(0f)] private float _holdSharpness = 18f;
        [Tooltip("Local presentation only: metres the held item bobs while walking.")]
        [SerializeField, Min(0f)] private float _swayAmplitude = 0.015f;

        private static readonly RaycastHit[] FocusHits = new RaycastHit[16];
        private static readonly RaycastHit[] HoldHits = new RaycastHit[16];

        // The replicated view pitch is only rewritten when it differs by at least this many degrees.
        private const int AimPitchStep = 2;
        private const float MaxAimPitch = 85f;

        // Held-item sweep: skin added to the cast so the pose never jumps, box shrink so flush contact does not count as a hit,
        // clearance kept between the item's underside and the floor, and how long the pitch keeps being published after a grab request.
        private const float HoldSkin = 0.04f;
        private const float HoldBoxScale = 0.9f;
        private const float GroundClearance = 0.03f;
        private const float GrabPublishSeconds = 0.5f;

        private InputAction _interact;
        private PlayerMovement _movement;
        private PlayerCharacter _character;
        private CharacterController _controller;
        private GnomeStack _stack;
        private bool _isSpawned;
        private float _lastCameraPitch;
        private float _publishPitchUntil;

        /// <summary>
        /// View pitch in whole degrees, written by the owner only while it holds something (nobody else needs it otherwise).
        /// Proxies use it to pose the held item in front of the holder's view.
        /// </summary>
        [Networked] private sbyte AimPitch { get; set; }

        public static PlayerInteractor Local { get; private set; }

        public Transform CarryAnchor => _carryAnchor;

        /// <summary>The movement component; only assigned on the owner (null on proxies).</summary>
        public PlayerMovement Movement => _movement;

        /// <summary>World position of the holder's eyes (the camera anchor).</summary>
        public Vector3 EyePosition
        {
            get
            {
                if (_eye != null)
                    return _eye.position;
                // Fallbacks without a camera anchor: the top of the character capsule, else the carry anchor, else the root.
                if (_controller != null)
                    return transform.TransformPoint(_controller.center + Vector3.up * (_controller.height * 0.5f));
                return _carryAnchor != null ? _carryAnchor.position : transform.position;
            }
        }

        /// <summary>Layers a held item retracts from.</summary>
        public LayerMask HoldObstacleMask => _holdObstacleMask;

        /// <summary>Local presentation: how fast the held item catches up with the view.</summary>
        public float HoldSharpness => _holdSharpness;

        /// <summary>Local presentation: metres the held item bobs while walking.</summary>
        public float SwayAmplitude => _swayAmplitude;
        public float Reach => _reach;
        public PlayerRole Role => _character.Role;

        /// <summary>What the replicated state says this player holds. Game logic reads this one; it never includes a prediction.</summary>
        public Carryable Carried => Carryable.FindHeldBy(this);

        /// <summary>
        /// HUD only: what the local presentation shows in the hands, counting a grab that is predicted but not confirmed yet
        /// and ignoring an item that was just released or placed. Never use it for game rules.
        /// </summary>
        public Carryable CarriedOrPredicted => Carryable.FindHeldOrPredictedBy(this);
        public IInteractable Focused { get; private set; }

        /// <summary>The cooked sausage this player is holding and can eat, if any.</summary>
        public Sausage EdibleCarried
        {
            get
            {
                var carried = Carried;
                return carried != null && carried.TryGetComponent<Sausage>(out var sausage) && sausage.CanBeEatenBy(this)
                    ? sausage
                    : null;
            }
        }

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            TryGetComponent(out _stack); // Only gnomes can stack.
            TryGetComponent(out _controller);
            if (_eye == null)
                _eye = FindByName(transform, "CameraAnchor");
            if (_eye == null)
                Debug.LogWarning($"{name}: no CameraAnchor found; held items are posed from the player root.", this);
        }

        // Runs once in Awake; prefabs are not edited, so the anchor is found by name.
        private static Transform FindByName(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            if (!HasStateAuthority)
                return;

            Local = this;
            _interact = InputSystem.actions.FindAction("Player/Interact");
            _movement = GetComponent<PlayerMovement>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            if (Local == this)
                Local = null;
        }

        /// <summary>Local player only: asked to take an item, so the view pitch is published right away for its holder pose.</summary>
        public void NoteGrabRequest() => _publishPitchUntil = Time.time + GrabPublishSeconds;

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || FirstPersonCamera.Instance == null)
                return;

            // Nobody needs the pitch unless an item is held, about to be (looking at one) or just requested.
            if (Carried == null && Focused is not Carryable && Time.time >= _publishPitchUntil)
                return;

            // Yaw already travels with the NetworkTransform; the pitch only lives on the camera, so publish it, but only
            // on a real change and once more when the view stops (so proxies converge).
            var pitch = Mathf.Clamp(Mathf.Round(FirstPersonCamera.Instance.Pitch), -MaxAimPitch, MaxAimPitch);
            var delta = Mathf.Abs(pitch - AimPitch);
            if (delta >= AimPitchStep || (delta > 0f && Mathf.Approximately(pitch, _lastCameraPitch)))
                AimPitch = (sbyte)pitch;

            _lastCameraPitch = pitch;
        }

        /// <summary>
        /// Where a single holder keeps <paramref name="item"/>: in front of the eyes, following yaw and (clamped) pitch.
        /// <paramref name="presentation"/> also lifts the pose off the floor and retracts it from geometry in the way,
        /// so the item never crosses a wall; without it the pose is the raw hold pose. No smoothing either way (that is
        /// local drawing only). The owner uses the exact camera angles, proxies the replicated pitch.
        /// </summary>
        public bool TryGetHoldPose(Carryable item, bool presentation, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (!_isSpawned || item == null)
                return false;

            float pitch, yaw;
            var view = Local == this ? FirstPersonCamera.Instance : null;
            if (view != null)
            {
                pitch = view.Pitch;
                yaw = _movement != null && _movement.IsFrozen ? transform.eulerAngles.y : view.Yaw;
            }
            else
            {
                pitch = AimPitch;
                yaw = transform.eulerAngles.y;
            }

            var eye = EyePosition;
            var eyeHeight = Mathf.Max(0.1f, eye.y - transform.position.y);
            var half = item.HoldHalfExtents;
            var aim = Quaternion.Euler(Mathf.Clamp(pitch, _minHoldPitch, _maxHoldPitch), yaw, 0f);

            // Lower by half the item's height so its top edge does not cover the aim.
            var drop = eyeHeight * _holdDropFactor + half.y;
            position = eye + aim * new Vector3(0f, -drop, eyeHeight * _holdDistanceFactor + item.HoldRadius);
            rotation = aim * Quaternion.Euler(item.HoldEuler);

            if (presentation)
            {
                position = LiftAboveGround(eye, position, rotation, item);
                position = RetractFromObstacles(eye, position, rotation, item);
            }

            return true;
        }

        // Raises the pose so the item's underside stays above the floor below it, instead of retracting against the floor.
        private Vector3 LiftAboveGround(Vector3 eye, Vector3 pose, Quaternion rotation, Carryable item)
        {
            var centerOffset = rotation * item.HoldCenter;
            var origin = new Vector3(pose.x, Mathf.Max(eye.y, pose.y), pose.z);
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, HoldHits, 4f, _holdObstacleMask, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            var groundY = 0f;
            for (var i = 0; i < count; i++)
            {
                if (HoldHits[i].distance > 0f && HoldHits[i].distance < nearest && !IgnoresForHold(HoldHits[i].collider, item))
                {
                    nearest = HoldHits[i].distance;
                    groundY = HoldHits[i].point.y;
                }
            }

            if (nearest == float.MaxValue)
                return pose;

            var bottom = pose.y + centerOffset.y - item.HoldHalfExtents.y;
            var missing = groundY + GroundClearance - bottom;
            if (missing > 0f)
                pose.y += missing;
            return pose;
        }

        // Sweeps the item's oriented box (a little smaller) from the eye to the pose and stops in front of the first
        // obstacle. The cast reaches HoldSkin past the pose, so an obstacle entering at the edge moves the pose smoothly.
        private Vector3 RetractFromObstacles(Vector3 eye, Vector3 pose, Quaternion rotation, Carryable item)
        {
            var toPose = pose - eye;
            var distance = toPose.magnitude;
            if (distance < 1e-4f)
                return pose;

            var direction = toPose / distance;
            var center = eye + rotation * item.HoldCenter;
            var count = Physics.BoxCastNonAlloc(center, item.HoldHalfExtents * HoldBoxScale, direction, HoldHits, rotation,
                distance + HoldSkin, _holdObstacleMask, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                // A distance of 0 means the box was born overlapping that collider: no usable contact point.
                if (HoldHits[i].distance > 0f && HoldHits[i].distance < nearest && !IgnoresForHold(HoldHits[i].collider, item))
                    nearest = HoldHits[i].distance;
            }

            if (nearest - HoldSkin >= distance)
                return pose;

            var minDistance = Mathf.Max(_minHoldDistance, item.HoldHalfExtents.z + 0.05f);
            return eye + direction * Mathf.Min(distance, Mathf.Max(minDistance, nearest - HoldSkin));
        }

        /// <summary>Whether a collider is not geometry for a held item: this player, other players and the item itself.</summary>
        public bool IgnoresForHold(Collider collider, Carryable item) =>
            collider.transform.IsChildOf(item.transform) || collider.GetComponentInParent<PlayerInteractor>() != null;

        private void Update()
        {
            if (Local != this)
                return;

            // Every early return below clears Focused, which also cancels a hold in progress (a kick stuns, so it restarts it).
            if (Focused == null && _holdTarget != null)
                ResetHold();

            // Typing in a text field: gameplay keys are ignored (like GnomePing).
            if (FirstPersonCamera.IsTyping)
            {
                Focused = null;
                return;
            }

            // Stunned or frozen (watched by the camper) players can't pick anything up or drop it.
            if (_movement != null && (_movement.IsStunned || _movement.IsFrozen))
            {
                Focused = null;
                return;
            }

            // A grab the authority has not confirmed yet: the item is already drawn in the hands, so the interact key does
            // nothing until then (and the item behind it must not be focused, or a double press would take two).
            if (Carried == null && CarriedOrPredicted != null)
            {
                Focused = null;
                return;
            }

            // Riding on another gnome's head: nothing to pick up, but what is already in the hands can still be used.
            if (_stack != null && _stack.IsRiding)
            {
                Focused = null;
                if (_interact != null && _interact.WasPressedThisFrame())
                    UseCarried(Carried);
                return;
            }

            UpdateFocus();

            // The action has a Hold interaction; WasPressedThisFrame and IsPressed ignore it.
            if (_interact == null)
                return;

            // "Hold E" targets are measured here; a tap on them does nothing (the progress just starts).
            if (TickHold())
                return;

            if (!_interact.WasPressedThisFrame())
                return;

            if (Focused != null && Focused.CanInteract(this))
            {
                NoteInteraction();
                Focused.Interact(this);
            }
            else
                UseCarried(Carried);
        }

        // Usability telemetry: seconds from the match start to this player's first interaction (once per match file).
        private static void NoteInteraction()
        {
            if (Telemetry.Enabled && MatchManager.Instance != null && MatchManager.Instance.Phase == MatchPhase.Playing && Telemetry.FirstThisMatch("first_interaction"))
                Telemetry.Begin("first_interaction").Float("seconds", Telemetry.T).Send();
        }

        /// <summary>Local player: how far the current "hold E" is, 0 to 1 (0 when nothing is being held).</summary>
        public float HoldProgress { get; private set; }

        /// <summary>Local player: <see cref="HoldProgress"/> in steps of 10 % (0 to 100), so the HUD only rewrites text on a step.</summary>
        public int HoldPercentStep => (int)(HoldProgress * 10f) * 10;

        private IHoldInteractable _holdTarget;
        private float _holdStart;

        private void ResetHold()
        {
            _holdTarget = null;
            HoldProgress = 0f;
        }

        // Returns true while the focused interactable is a hold target for this player (the tap path must not run).
        // The hold starts only on a fresh press while looking at the target, and restarts if the focus changes, the key is
        // released or the target stops accepting this player.
        private bool TickHold()
        {
            if (Focused is not IHoldInteractable hold || hold.GetHoldTime(this) <= 0f)
            {
                ResetHold();
                return false;
            }

            if (!Focused.CanInteract(this))
            {
                ResetHold();
                return false;
            }

            if (_holdTarget != hold)
            {
                ResetHold();
                if (_interact.WasPressedThisFrame())
                {
                    _holdTarget = hold;
                    _holdStart = Time.time;
                }

                return true;
            }

            if (!_interact.IsPressed())
            {
                ResetHold();
                return true;
            }

            var time = hold.GetHoldTime(this);
            HoldProgress = Mathf.Clamp01((Time.time - _holdStart) / time);
            if (HoldProgress >= 1f)
            {
                ResetHold();
                NoteInteraction();
                hold.OnHoldComplete(this);
            }

            return true;
        }

        // The item's own use first (e.g. fill the bucket), then put the log on the campfire or pour the bucket on it
        // if it is in reach, otherwise eat it (sausage) or let it go.
        private void UseCarried(Carryable carried)
        {
            if (carried == null)
                return;

            NoteInteraction();
            if (carried.TryGetComponent<ICarriedUse>(out var use) && use.TryUse(this))
                return;

            var fire = FireplaceManager.Instance;
            if (fire != null && fire.CanPlace(this))
            {
                fire.RequestPlace(this);
                return;
            }

            if (fire != null && fire.CanPour(this))
            {
                fire.RequestPour(this);
                return;
            }

            // A full bucket in reach of the fire but blocked (grace, a pour in progress) is kept: E must not drop it.
            if (fire != null && fire.PourBlockedMessage(this) != null)
                return;

            var edible = EdibleCarried;
            if (edible != null)
                edible.RequestEat(this);
            else
                carried.RequestRelease(this);
        }

        // Props (e.g. teepee logs) can stand in front of what the player wants: take the nearest hit that accepts the
        // player, and only if none does, the nearest one with an interactable (to show why it can't be used).
        private void UpdateFocus()
        {
            Focused = null;
            var view = FirstPersonCamera.Instance;
            if (view == null)
                return;

            var origin = view.transform;
            var count = Physics.SphereCastNonAlloc(origin.position, _aimRadius, origin.forward, FocusHits, _reach, _interactionMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(FocusHits, 0, count, HitDistanceComparer.Instance);

            for (var i = 0; i < count; i++)
            {
                if (FocusHits[i].transform.IsChildOf(transform))
                    continue;

                var candidate = PickInteractable(FocusHits[i].collider);
                if (candidate == null)
                    continue;

                if (candidate.CanInteract(this))
                {
                    Focused = candidate;
                    return;
                }

                Focused ??= candidate;
            }
        }

        private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new();

            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        // Nested interactables (e.g. the grill inside the campfire): prefer the first one that accepts this player right now.
        private IInteractable PickInteractable(Collider collider)
        {
            var candidates = collider.GetComponentsInParent<IInteractable>();
            foreach (var candidate in candidates)
            {
                if (candidate.CanInteract(this))
                    return candidate;
            }

            return candidates.Length > 0 ? candidates[0] : null;
        }
    }
}
