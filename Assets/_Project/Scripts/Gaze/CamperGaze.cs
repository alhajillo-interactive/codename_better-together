using System.Collections.Generic;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// The camper's petrifying gaze. Only one <see cref="Petrifiable"/> at a time is locked: the camper (state authority)
    /// picks the gnome closest to the view axis, keeps it while it stays inside a slightly wider cone, and the lock
    /// fills up during a short grace. Once full, the gnome's owner freezes it and the camper drops the lock to look for a free gnome (a statue stays
    /// stone until its player breaks free). The screen edges stay outside the cone,
    /// so gnomes can still move in the camper's peripheral vision.
    /// The gaze only works while the camper holds the focus button; focusing drains a stamina bar and slows the camper.
    /// </summary>
    public class CamperGaze : NetworkBehaviour
    {
        [Tooltip("Eye position; use the camera anchor.")]
        [SerializeField] private Transform _eye;
        [Tooltip("Half-angle of the petrifying cone. The camera sees ~45° to each side, so 22° leaves the edges free.")]
        [Range(1f, 90f)]
        [SerializeField] private float _halfAngle = 22f;
        [Tooltip("Base reach of the gaze in metres.")]
        [SerializeField] private float _range = 8f;
        [Tooltip("Layers that block the gaze (walls, tents, trees...).")]
        [SerializeField] private LayerMask _occluders = ~0;
        [SerializeField] private bool _enabledOnSpawn = true;

        [Header("Night ramp")]
        [Tooltip("Extra metres of range reached at the very end of the match.")]
        [SerializeField] private float _nightRangeBonus = 2f;
        [Tooltip("The bonus grows linearly over the last seconds of the match.")]
        [SerializeField] private float _nightRampSeconds = 30f;

        [Header("Lock")]
        [Tooltip("Seconds a gnome must stay locked before it freezes.")]
        [SerializeField] private float _lockGrace = 0.25f;
        [Tooltip("Seconds after losing a target before another one can be locked.")]
        [SerializeField] private float _retargetDelay = 0.3f;
        [Tooltip("Seconds a frozen target may stay out of sight before it is released.")]
        [SerializeField] private float _lostSightGrace = 0.3f;
        [Tooltip("Extra degrees of cone the current target keeps while locked.")]
        [SerializeField] private float _hysteresisAngle = 4f;
        [Tooltip("Extra metres of range the current target keeps while locked.")]
        [SerializeField] private float _hysteresisRange = 1f;
        [Tooltip("Candidate score = angle to the view axis (deg) + this × distance (m); lowest wins.")]
        [SerializeField] private float _distanceWeight = 0.5f;

        [Header("Focus")]
        [Tooltip("Seconds of continuous focus from a full bar to empty.")]
        [SerializeField] private float _staminaSeconds = 4f;
        [Tooltip("Seconds after releasing focus before the bar starts to refill.")]
        [SerializeField] private float _regenDelay = 1f;
        [Tooltip("Seconds to refill the bar from empty to full.")]
        [SerializeField] private float _regenSeconds = 6f;
        [Tooltip("After running dry, the gaze can't be used again until the bar reaches this fraction.")]
        [Range(0f, 1f)]
        [SerializeField] private float _reuseThreshold = 0.35f;
        [Tooltip("Walk and sprint speed multiplier while focusing.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _focusMoveMultiplier = 0.5f;

        private static readonly List<CamperGaze> _all = new();
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        private float _retargetTimer;
        private float _lostSightTime;
        private float _lastCameraPitch;
        private bool _resetRequested;
        private bool _isSpawned;
        private PlayerMovement _movement;
        private InputAction _focus;

        /// <summary>Every spawned camper gaze, local and remote.</summary>
        public static IReadOnlyList<CamperGaze> All => _all;

        /// <summary>The local camper's gaze (state authority).</summary>
        public static CamperGaze Local { get; private set; }

        public float HalfAngle => _halfAngle;

        // Pitch is only rewritten when it moves more than this many degrees, so mouse noise doesn't dirty it every tick.
        private const float PitchStep = 0.5f;

        // If the camera stops while the replicated pitch is still off by more than this, it is written once more so proxies converge to the exact value.
        private const float PitchSettle = 0.01f;

        [Networked] private float Pitch { get; set; }
        [Networked] private Petrifiable Target { get; set; }

        /// <summary>Tick at which <see cref="Target"/> was locked; the lock fill is derived from it. Meaningless while there is no target.</summary>
        [Networked] private int LockTick { get; set; }

        /// <summary>
        /// Written only by the state authority: true once its own derived lock fill reached 1 and the lock still holds;
        /// false when a target is locked, released or lost. Replicated so that no peer acts on a full lock the camper
        /// has not confirmed (see <see cref="LockProgress"/>).
        /// </summary>
        [Networked] private NetworkBool LockFull { get; set; }

        [Networked] public NetworkBool IsGazeEnabled { get; private set; }
        [Networked] private NetworkBool IsFocusing { get; set; }

        // Stamina is replicated as "value at the last event + tick of that event" (see CurrentStamina). The anchor is
        // rewritten only when focusing starts/stops, when the bar runs dry and when a match starts.
        [Networked] private float StaminaAnchor { get; set; }
        [Networked] private int StaminaTick { get; set; }
        [Networked] private NetworkBool IsExhausted { get; set; }

        /// <summary>True while the camper holds the focus button and has stamina; only then the gaze petrifies.</summary>
        public bool Focusing => _isSpawned && IsFocusing;

        /// <summary>0..1 gaze stamina.</summary>
        public float StaminaFraction => _isSpawned ? CurrentStamina : 1f;

        /// <summary>
        /// Stamina now, derived from the replicated anchor: it drains while focusing, and refills after a short delay otherwise.
        /// </summary>
        private float CurrentStamina
        {
            get
            {
                var elapsed = NetAnchor.Elapsed(Runner, StaminaTick);
                var value = IsFocusing
                    ? StaminaAnchor - elapsed / Mathf.Max(_staminaSeconds, 0.01f)
                    : StaminaAnchor + Mathf.Max(0f, elapsed - _regenDelay) / Mathf.Max(_regenSeconds, 0.01f);
                return Mathf.Clamp01(value);
            }
        }

        /// <summary>True after the stamina ran out, until it refills to <see cref="ReuseThreshold"/>.</summary>
        public bool Exhausted => _isSpawned && IsExhausted;

        /// <summary>Stamina fraction needed to use the gaze again after running dry.</summary>
        public float ReuseThreshold => _reuseThreshold;

        /// <summary>True once spawned with the gaze switched on, whether or not the camper is focusing.</summary>
        public bool IsGazeOn => _isSpawned && IsGazeEnabled;

        /// <summary>The gnome currently locked (or frozen) by this gaze; null if none.</summary>
        public Petrifiable LockedTarget => _isSpawned ? Target : null;

        /// <summary>
        /// 0..1 fill of the lock grace; at 1 the target is frozen. The fill is derived from <see cref="LockTick"/>, but a
        /// peer's tick can run ahead of the data it has received (the gnome that must freeze is a proxy of this gaze), so
        /// a derived fill would reach 1 while the camper has already let go, or before it even got the target. Therefore 1 is
        /// only returned while the authority has confirmed it with <see cref="LockFull"/>; until then it stays below 1.
        /// </summary>
        public float LockProgress
        {
            get
            {
                if (!_isSpawned || Target == null)
                    return 0f;

                return LockFull ? 1f : Mathf.Min(DerivedLockFill, 0.99f);
            }
        }

        // Lock fill from the elapsed ticks alone, unconfirmed. The authority uses it to decide LockFull.
        private float DerivedLockFill =>
            _lockGrace > 0f ? Mathf.Clamp01(NetAnchor.Elapsed(Runner, LockTick) / _lockGrace) : 1f;

        /// <summary>True once spawned with the gaze turned on and the camper focusing.</summary>
        public bool IsActive => _isSpawned && IsGazeEnabled && IsFocusing;

        /// <summary>Current reach: the base range plus the night bonus.</summary>
        public float Range => _range + NightRangeBonus;

        /// <summary>0..1 over the night ramp at the end of the match; 0 outside of it or without a match.</summary>
        public float NightFactor
        {
            get
            {
                var match = MatchManager.Instance;
                if (match == null || match.Phase != MatchPhase.Playing || _nightRampSeconds <= 0f)
                    return 0f;

                return Mathf.Clamp01(1f - match.TimeRemaining / _nightRampSeconds);
            }
        }

        private float NightRangeBonus => _nightRangeBonus * NightFactor;

        public Vector3 EyePosition => _eye != null ? _eye.position : transform.position + Vector3.up * 1.6f;

        // The authority aims with the exact camera pitch; proxies use the replicated one.
        public Vector3 LookDirection => Quaternion.Euler(CurrentPitch, transform.eulerAngles.y, 0f) * Vector3.forward;

        private float CurrentPitch
        {
            get
            {
                if (!_isSpawned)
                    return 0f;

                return HasStateAuthority && FirstPersonCamera.Instance != null ? FirstPersonCamera.Instance.Pitch : Pitch;
            }
        }

        private void Awake() => _movement = GetComponent<PlayerMovement>();

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            if (!HasStateAuthority)
                return;

            Local = this;
            IsGazeEnabled = _enabledOnSpawn;
            LockFull = false;
            StaminaAnchor = 1f;
            StaminaTick = Runner.Tick.Raw;
            IsExhausted = false;
            IsFocusing = false;
            _focus = InputSystem.actions.FindAction("Player/Focus");
            MatchManager.OnGameStarted += HandleGameStarted;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _all.Remove(this);
            MatchManager.OnGameStarted -= HandleGameStarted;
            if (Local == this)
            {
                Local = null;
                if (CameraFocus.Instance != null)
                    CameraFocus.Instance.SetFocused(false);
            }
        }

        // Raised from a render-side callback, so only flag it; the networked writes happen in FixedUpdateNetwork.
        private void HandleGameStarted() => _resetRequested = true;

        public override void Render()
        {
            if (HasStateAuthority && CameraFocus.Instance != null)
                CameraFocus.Instance.SetFocused(IsFocusing);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            // Yaw already travels with the NetworkTransform; pitch only lives on the camera, so publish it.
            if (FirstPersonCamera.Instance != null)
            {
                var pitch = FirstPersonCamera.Instance.Pitch;
                var delta = Mathf.Abs(pitch - Pitch);
                if (delta > PitchStep || (delta > PitchSettle && Mathf.Approximately(pitch, _lastCameraPitch)))
                    Pitch = pitch;

                _lastCameraPitch = pitch;
            }

            var dt = Runner.DeltaTime;
            UpdateFocus();
            UpdateLock(dt);
        }

        // Stamina is never written per tick: it is re-anchored only when focusing flips, when the bar runs dry and when a match starts.
        private void UpdateFocus()
        {
            var tick = Runner.Tick.Raw;
            if (_resetRequested)
            {
                _resetRequested = false;
                StaminaAnchor = 1f;
                StaminaTick = tick;
                IsExhausted = false;
            }

            var stamina = CurrentStamina;
            var wantsFocus = _focus != null && _focus.IsPressed() && Cursor.lockState == CursorLockMode.Locked;
            var focusing = wantsFocus && !IsExhausted && stamina > 0f && IsGazeEnabled;
            if (IsFocusing && stamina <= 0f)
            {
                // The bar ran dry while focusing.
                IsExhausted = true;
                focusing = false;
            }
            else if (!focusing && IsExhausted && stamina >= _reuseThreshold)
            {
                IsExhausted = false;
            }

            if (focusing != IsFocusing)
            {
                StaminaAnchor = stamina;
                StaminaTick = tick;
                IsFocusing = focusing;
            }

            if (_movement != null)
                _movement.SpeedMultiplier = focusing ? _focusMoveMultiplier : 1f;
        }

        /// <summary>Turns the gaze on or off. Anyone may call it (debug panel); the camper's owner applies it.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SetGazeEnabled(NetworkBool enabled) => IsGazeEnabled = enabled;

        /// <summary>
        /// True if <paramref name="point"/> (belonging to <paramref name="target"/>) is in the cone and not hidden.
        /// The extra values widen the cone and range, e.g. to keep a lock that is already established.
        /// </summary>
        public bool CanSee(Vector3 point, Transform target, float extraAngle = 0f, float extraRange = 0f)
        {
            if (!IsActive)
                return false;

            var eye = EyePosition;
            var toPoint = point - eye;
            var distance = toPoint.magnitude;
            if (distance > Range + extraRange || Vector3.Angle(LookDirection, toPoint) > _halfAngle + extraAngle)
                return false;

            // Blocked by scenery, but not by the target itself, the camper, or items (a gnome can't hide behind its log).
            var count = Physics.RaycastNonAlloc(eye, toPoint / distance, _hits, distance, _occluders, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i].transform;
                if (!hit.IsChildOf(target) && !hit.IsChildOf(transform) && hit.GetComponentInParent<Carryable>() == null)
                    return false;
            }

            return true;
        }

        private void UpdateLock(float dt)
        {
            if (!IsGazeEnabled)
            {
                ClearLock();
                return;
            }

            if (!IsFocusing)
            {
                ReleaseLock();
                return;
            }

            var current = Target;
            if (current != null && TryKeep(current, dt))
            {
                // The fill itself is derived from LockTick; only the confirmation of a full lock is written, once.
                if (!LockFull && DerivedLockFill >= 1f)
                    LockFull = true;
                return;
            }

            if (current != null)
            {
                Target = null;
                LockFull = false;
                _retargetTimer = _retargetDelay;
                _lostSightTime = 0f;
            }
            else if (LockFull)
            {
                // The locked gnome left the game: its reference reads as null without passing through the branch above.
                LockFull = false;
            }

            _retargetTimer -= dt;
            if (_retargetTimer > 0f)
                return;

            var best = FindBestCandidate();
            if (best == null)
                return;

            Target = best;
            LockTick = Runner.Tick.Raw;
            LockFull = false;
            _lostSightTime = 0f;
        }

        // Same as losing sight: the gnome is released at once.
        private void ReleaseLock()
        {
            Target = null;
            LockFull = false;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
        }

        private void ClearLock()
        {
            Target = null;
            LockFull = false;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
        }

        // The current target is kept inside the widened cone; a frozen one also survives a short moment out of sight.
        private bool TryKeep(Petrifiable target, float dt)
        {
            if (!target.IsAvailable || target.IsGazeImmune)
                return false;

            // Full lock and the gnome already turned to stone: let go so the gaze can move on to a free gnome.
            var locked = DerivedLockFill >= 1f; // Authority only: its own fill, not the one confirmed to peers.
            if (locked && target.IsStone)
                return false;

            if (target.IsVisibleTo(this, _hysteresisAngle, _hysteresisRange))
            {
                _lostSightTime = 0f;
                return true;
            }

            if (!locked)
                return false;

            _lostSightTime += dt;
            return _lostSightTime <= _lostSightGrace;
        }

        private Petrifiable FindBestCandidate()
        {
            var eye = EyePosition;
            var look = LookDirection;
            Petrifiable best = null;
            var bestScore = float.MaxValue;
            foreach (var candidate in Petrifiable.All)
            {
                if (candidate == null || !candidate.IsAvailable || candidate.IsGazeImmune || candidate.IsStone)
                    continue;

                if (!candidate.IsVisibleTo(this))
                    continue;

                var toFocus = candidate.FocusPoint - eye;
                var score = Vector3.Angle(look, toFocus) + _distanceWeight * toFocus.magnitude;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = candidate;
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            var eye = EyePosition;
            var forward = Object != null && Object.IsValid ? LookDirection : transform.forward;
            var length = Object != null && Object.IsValid ? Range : _range;
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
            foreach (var axis in new[] { Vector3.up, transform.right })
            {
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(_halfAngle, axis) * forward * length);
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(-_halfAngle, axis) * forward * length);
            }
        }
    }
}
