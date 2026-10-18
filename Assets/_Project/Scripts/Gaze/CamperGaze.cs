using System.Collections.Generic;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// The camper's petrifying gaze. Only one <see cref="Petrifiable"/> at a time is locked: the camper (state authority)
    /// picks the gnome closest to the view axis, keeps it while it stays inside a slightly wider cone, and the lock
    /// fills up during a short grace. Once full, the gnome's owner freezes it. The screen edges stay outside the cone,
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
        private float _sinceFocus;
        private bool _isSpawned;
        private PlayerMovement _movement;
        private InputAction _focus;

        /// <summary>Every spawned camper gaze, local and remote.</summary>
        public static IReadOnlyList<CamperGaze> All => _all;

        /// <summary>The local camper's gaze (state authority).</summary>
        public static CamperGaze Local { get; private set; }

        public float HalfAngle => _halfAngle;

        [Networked] private float Pitch { get; set; }
        [Networked] private Petrifiable Target { get; set; }
        [Networked] private float Progress { get; set; }
        [Networked] public NetworkBool IsGazeEnabled { get; private set; }
        [Networked] private NetworkBool IsFocusing { get; set; }
        [Networked] private float Stamina { get; set; }
        [Networked] private NetworkBool IsExhausted { get; set; }

        /// <summary>True while the camper holds the focus button and has stamina; only then the gaze petrifies.</summary>
        public bool Focusing => _isSpawned && IsFocusing;

        /// <summary>0..1 gaze stamina.</summary>
        public float StaminaFraction => _isSpawned ? Stamina : 1f;

        /// <summary>True after the stamina ran out, until it refills to <see cref="ReuseThreshold"/>.</summary>
        public bool Exhausted => _isSpawned && IsExhausted;

        /// <summary>Stamina fraction needed to use the gaze again after running dry.</summary>
        public float ReuseThreshold => _reuseThreshold;

        /// <summary>True once spawned with the gaze switched on, whether or not the camper is focusing.</summary>
        public bool IsGazeOn => _isSpawned && IsGazeEnabled;

        /// <summary>The gnome currently locked (or frozen) by this gaze; null if none.</summary>
        public Petrifiable LockedTarget => _isSpawned ? Target : null;

        /// <summary>0..1 fill of the lock grace; at 1 the target is frozen.</summary>
        public float LockProgress => _isSpawned ? Progress : 0f;

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

        public Vector3 LookDirection => Quaternion.Euler(_isSpawned ? Pitch : 0f, transform.eulerAngles.y, 0f) * Vector3.forward;

        private void Awake() => _movement = GetComponent<PlayerMovement>();

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            if (!HasStateAuthority)
                return;

            Local = this;
            IsGazeEnabled = _enabledOnSpawn;
            Stamina = 1f;
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

        private void HandleGameStarted()
        {
            Stamina = 1f;
            IsExhausted = false;
            _sinceFocus = 0f;
        }

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
                Pitch = FirstPersonCamera.Instance.Pitch;

            var dt = Runner.DeltaTime;
            UpdateFocus(dt);
            UpdateLock(dt);
        }

        private void UpdateFocus(float dt)
        {
            var wantsFocus = _focus != null && _focus.IsPressed() && Cursor.lockState == CursorLockMode.Locked;
            var focusing = wantsFocus && !IsExhausted && Stamina > 0f && IsGazeEnabled;
            if (focusing)
            {
                _sinceFocus = 0f;
                Stamina = Mathf.Max(0f, Stamina - dt / Mathf.Max(_staminaSeconds, 0.01f));
                if (Stamina <= 0f)
                {
                    IsExhausted = true;
                    focusing = false;
                }
            }
            else
            {
                _sinceFocus += dt;
                if (_sinceFocus >= _regenDelay)
                    Stamina = Mathf.Min(1f, Stamina + dt / Mathf.Max(_regenSeconds, 0.01f));

                if (IsExhausted && Stamina >= _reuseThreshold)
                    IsExhausted = false;
            }

            IsFocusing = focusing;
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
                ReleaseLock(dt);
                return;
            }

            var current = Target;
            if (current != null && TryKeep(current, dt))
            {
                Progress = _lockGrace > 0f ? Mathf.Min(1f, Progress + dt / _lockGrace) : 1f;
                return;
            }

            if (current != null)
            {
                Target = null;
                _retargetTimer = _retargetDelay;
                _lostSightTime = 0f;
            }

            Progress = Mathf.Max(0f, Progress - 2f * dt / Mathf.Max(_lockGrace, 0.01f));
            _retargetTimer -= dt;
            if (_retargetTimer > 0f)
                return;

            var best = FindBestCandidate();
            if (best == null)
                return;

            Target = best;
            Progress = 0f;
            _lostSightTime = 0f;
        }

        // Same as losing sight: the gnome is released at once, and the lock drains.
        private void ReleaseLock(float dt)
        {
            Target = null;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
            Progress = Mathf.Max(0f, Progress - 2f * dt / Mathf.Max(_lockGrace, 0.01f));
        }

        private void ClearLock()
        {
            Target = null;
            Progress = 0f;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
        }

        // The current target is kept inside the widened cone; a frozen one also survives a short moment out of sight.
        private bool TryKeep(Petrifiable target, float dt)
        {
            if (!target.IsAvailable || target.IsGazeImmune)
                return false;

            if (target.IsVisibleTo(this, _hysteresisAngle, _hysteresisRange))
            {
                _lostSightTime = 0f;
                return true;
            }

            if (Progress < 1f)
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
                if (candidate == null || !candidate.IsAvailable || candidate.IsGazeImmune || IsFrozenByOtherGaze(candidate))
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

        private bool IsFrozenByOtherGaze(Petrifiable candidate)
        {
            foreach (var gaze in _all)
            {
                if (gaze != this && gaze != null && gaze.LockedTarget == candidate && gaze.LockProgress >= 1f)
                    return true;
            }

            return false;
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
