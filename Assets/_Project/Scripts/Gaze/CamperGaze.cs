using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Fireplace;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Networking;
using BetterTogether.Player;
using BetterTogether.Diagnostics;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Gaze
{
    /// <summary>How the camera shutter fires: on its own when the focus completes, or on the camper's click.</summary>
    public enum ShutterMode : byte
    {
        /// <summary>Variant A: the photo is taken by itself when the focus completes.</summary>
        Auto,
        /// <summary>Variant B: focusing only prepares the shot; the click takes it (a click made too early is kept).</summary>
        Click,
    }

    /// <summary>
    /// The camper's petrifying camera (class name kept from the old gaze). Only one <see cref="Petrifiable"/> at a time is locked: the camper (state authority)
    /// picks the gnome closest to the view axis, keeps it while it stays inside a slightly wider cone, and the lock
    /// fills up during a short grace. Once full, the gnome's owner freezes it and the camper drops the lock to look for a free gnome (a statue stays
    /// stone until its player breaks free). The screen edges stay outside the cone,
    /// so gnomes can still move in the camper's peripheral vision.
    /// The camera only works while the camper holds the focus button (raising it is free but slows the camper). Shots spend
    /// plates that recharge on their own; in <see cref="ShutterMode.Click"/> only the click freezes the focused gnome.
    /// </summary>
    public class CamperGaze : NetworkBehaviour
    {
        [Tooltip("Eye position; use the camera anchor.")]
        [SerializeField] private Transform _eye;
        [Tooltip("Half-angle of the petrifying cone. The camera sees ~45° to each side, so 22° leaves the edges free.")]
        [Range(1f, 90f)]
        [SerializeField] private float _halfAngle = 22f;
        [Tooltip("Half-angle of the cone while the camper's comeback bonus is active (see FireplaceManager).")]
        [Range(1f, 90f)]
        [SerializeField] private float _comebackHalfAngle = 26f;
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
        [Tooltip("Walk and sprint speed multiplier while focusing.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _focusMoveMultiplier = 0.5f;

        [Header("Plates and shutter")]
        [Tooltip("Plates a full camera holds; every shot spends one.")]
        [Min(1)]
        [SerializeField] private int _maxPlates = 3;
        [Tooltip("Seconds to recharge one plate.")]
        [SerializeField] private float _plateRechargeSeconds = 4f;
        [Tooltip("Seconds after a shot before plates start recharging.")]
        [SerializeField] private float _plateRegenDelay = 1f;
        [Tooltip("Minimum seconds between two shots (max 3 flashes per second at 0.4).")]
        [SerializeField] private float _minShotInterval = 0.4f;
        [Tooltip("Shutter mode the camper starts with: Click = variant B (default), Auto = variant A.")]
        [SerializeField] private ShutterMode _defaultMode = ShutterMode.Click;

        [Header("Audio (empty cues are skipped; for the audio team)")]
        [Tooltip("Wood creak when the camera is raised. Heard by everyone, from the camper's eye. For the audio team.")]
        [SerializeField] private SoundCue _raiseSfx;
        [Tooltip("Shutter click of a shot. Heard by everyone, from the camper's eye. For the audio team.")]
        [SerializeField] private SoundCue _shutterSfx;
        [Tooltip("Dry click when shooting with no plates. Only the camper hears it. For the audio team.")]
        [SerializeField] private SoundCue _drySfx;
        [Tooltip("A plate finished recharging. Only the camper hears it. For the audio team.")]
        [SerializeField] private SoundCue _plateReadySfx;

        private static readonly List<CamperGaze> _all = new();
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        private float _retargetTimer;
        private float _lostSightTime;
        private float _lastCameraPitch;
        private bool _resetRequested;
        private bool _isSpawned;
        private PlayerMovement _movement;
        private PlayerInteractor _interactor;
        private InputAction _focus;
        private InputAction _attack;

        // Authority only: a shot click read in Update, consumed in FixedUpdateNetwork, and the click kept until the focus completes.
        private bool _attackQueued;
        private bool _bufferedShot;
        private Petrifiable _bufferedTarget;

        // Render-side previous values for the sound cues; the first Render after Spawned only initialises them.
        private bool _feedbackReady;
        private bool _prevFocusing;
        private byte _prevShotSeq;
        private byte _prevDrySeq;
        private int _prevWholePlates;

        /// <summary>Every spawned camper gaze, local and remote.</summary>
        public static IReadOnlyList<CamperGaze> All => _all;

        /// <summary>The local camper's gaze (state authority).</summary>
        public static CamperGaze Local { get; private set; }

        /// <summary>Current cone half-angle: widened while the replicated comeback timer runs (the camper decides on their own gaze).</summary>
        public float HalfAngle
        {
            get
            {
                var fire = FireplaceManager.Instance;
                return fire != null && fire.ComebackActive ? Mathf.Max(_halfAngle, _comebackHalfAngle) : _halfAngle;
            }
        }

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

        // Plates are replicated as "value at the last event + tick of that event" (see CurrentPlates). The anchor is
        // rewritten only when a shot is fired, when a match starts and on spawn.
        [Networked] private float PlatesAnchor { get; set; }
        [Networked] private int PlatesTick { get; set; }
        [Networked] private int NetLastShotTick { get; set; }
        [Networked] private byte NetShotSeq { get; set; }
        [Networked] private byte NetDrySeq { get; set; }
        [Networked] private NetworkBool NetLastShotHit { get; set; }
        [Networked] private ShutterMode NetMode { get; set; }

        /// <summary>True while the camper holds the focus button with the camera active; only then the camera can freeze a gnome.</summary>
        public bool Focusing => _isSpawned && IsFocusing;

        /// <summary>Plates now (0..<see cref="MaxPlates"/>), derived from the replicated anchor: they recharge after a short delay.</summary>
        public float Plates => _isSpawned ? CurrentPlates : _maxPlates;

        /// <summary>Plates a full camera holds.</summary>
        public int MaxPlates => _maxPlates;

        /// <summary>0..1 progress of the plate being recharged; 1 when every plate is loaded.</summary>
        public float NextPlateFraction
        {
            get
            {
                var plates = Plates;
                return plates >= _maxPlates ? 1f : plates - Mathf.Floor(plates);
            }
        }

        private float CurrentPlates
        {
            get
            {
                var elapsed = NetAnchor.Elapsed(Runner, PlatesTick);
                var value = PlatesAnchor + Mathf.Max(0f, elapsed - _plateRegenDelay) / Mathf.Max(_plateRechargeSeconds, 0.01f);
                return Mathf.Clamp(value, 0f, _maxPlates);
            }
        }

        /// <summary>Shots taken so far (wraps at 256); a change means a new shutter click. Compare, don't count.</summary>
        public byte ShotSeq => _isSpawned ? NetShotSeq : (byte)0;

        /// <summary>Dry clicks (no plates) so far (wraps at 256).</summary>
        public byte DrySeq => _isSpawned ? NetDrySeq : (byte)0;

        /// <summary>True if the last shot froze a gnome.</summary>
        public bool LastShotHit => _isSpawned && NetLastShotHit;

        /// <summary>Simulation tick of the last shot (a made-up past tick before the first one). Use <see cref="ShotSeq"/> to detect shots.</summary>
        public int LastShotTick => _isSpawned ? NetLastShotTick : 0;

        /// <summary>Current shutter mode (A = Auto, B = Click).</summary>
        public ShutterMode Mode => _isSpawned ? NetMode : _defaultMode;

        /// <summary>True when the camera may be raised: gaze on and nothing in the camper's hands.</summary>
        public bool CanRaise => _isSpawned && IsGazeEnabled && (_interactor == null || _interactor.CarriedOrPredicted == null);

        /// <summary>
        /// A gnome is focused and the lock is complete but not shot yet. Only meaningful on the camper's own client (the authority
        /// derives it from its own tick): it is the HUD's "ready to shoot". False with no plate left.
        /// </summary>
        public bool FocusReady => _isSpawned && Target != null && DerivedLockFill >= 1f && !LockFull && CurrentPlates >= 1f;

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

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _interactor = GetComponent<PlayerInteractor>();
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            if (!HasStateAuthority)
                return;

            Local = this;
            IsGazeEnabled = _enabledOnSpawn;
            LockFull = false;
            FillPlates(Runner.Tick.Raw);
            NetLastShotTick = Runner.Tick.Raw - NoShotTicks;
            NetMode = _defaultMode;
            IsFocusing = false;
            _focus = InputSystem.actions.FindAction("Player/Focus");
            _attack = InputSystem.actions.FindAction("Player/Attack");
            MatchManager.OnGameStarted += HandleGameStarted;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _feedbackReady = false;
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

        // Reads the shot click here (input is per frame); only the flag is kept, FixedUpdateNetwork decides and writes.
        private void Update()
        {
            if (!_isSpawned || !HasStateAuthority || _attack == null)
                return;

            // Left click also re-locks the cursor (FirstPersonCamera), so ignore it while the cursor is free.
            if (IsFocusing && Cursor.lockState == CursorLockMode.Locked && _attack.WasPressedThisFrame())
                _attackQueued = true;
        }

        public override void Render()
        {
            if (!_isSpawned)
                return;

            if (HasStateAuthority && CameraFocus.Instance != null)
                CameraFocus.Instance.SetFocused(IsFocusing);

            PlayFeedback();
        }

        // Sounds come from replicated state changes, never from RPCs. Late joiners only initialise the previous values.
        private void PlayFeedback()
        {
            var focusing = IsFocusing;
            var shotSeq = NetShotSeq;
            var drySeq = NetDrySeq;
            var wholePlates = Mathf.FloorToInt(CurrentPlates);
            if (_feedbackReady)
            {
                if (focusing && !_prevFocusing)
                    _raiseSfx.PlayAt(EyePosition);

                if (shotSeq != _prevShotSeq)
                    _shutterSfx.PlayAt(EyePosition);

                if (HasStateAuthority)
                {
                    if (drySeq != _prevDrySeq)
                        _drySfx.Play();

                    // One plate recharged; a refill at match start jumps several at once and stays silent.
                    if (wholePlates == _prevWholePlates + 1)
                        _plateReadySfx.Play();
                }
            }

            _feedbackReady = true;
            _prevFocusing = focusing;
            _prevShotSeq = shotSeq;
            _prevDrySeq = drySeq;
            _prevWholePlates = wholePlates;
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
            UpdateShutter();
        }

        // Ticks before "now" used as the last shot at spawn, so the first shot is never blocked by the interval.
        private const int NoShotTicks = 10000;

        private void FillPlates(int tick)
        {
            PlatesAnchor = _maxPlates;
            PlatesTick = tick;
        }

        // Raising the camera costs nothing; it is only refused while the camper carries something.
        private void UpdateFocus()
        {
            if (_resetRequested)
            {
                _resetRequested = false;
                _bufferedShot = false;
                FillPlates(Runner.Tick.Raw);
            }

            var wantsFocus = _focus != null && _focus.IsPressed() && Cursor.lockState == CursorLockMode.Locked;
            var focusing = wantsFocus && CanRaise;
            if (focusing != IsFocusing)
                IsFocusing = focusing;

            if (_movement != null)
                _movement.SpeedMultiplier = focusing ? _focusMoveMultiplier : 1f;
        }

        // Shots are the only thing that spends plates and, in Click mode, the only thing that freezes a gnome.
        private void UpdateShutter()
        {
            var pressed = _attackQueued;
            _attackQueued = false;
            if (!IsFocusing || !IsGazeEnabled)
            {
                _bufferedShot = false;
                return;
            }

            var target = Target;
            if (NetMode == ShutterMode.Auto)
            {
                // Variant A: the photo is taken when the focus completes; with no plates the camera just stays focused.
                if (target != null && !LockFull && DerivedLockFill >= 1f && CurrentPlates >= 1f && ShotIntervalPassed)
                    FireShot(true);
                return;
            }

            if (pressed)
            {
                if (target == null)
                {
                    // No gnome focused: the click fires at once and misses. Never kept.
                    _bufferedShot = false;
                    if (ShotIntervalPassed)
                        FireShot(false);
                    return;
                }

                _bufferedShot = true;
                _bufferedTarget = target;
            }

            if (!_bufferedShot)
                return;

            // A kept click belongs to the gnome it was aimed at: if that one got away (or another took its place), drop it.
            if (target != _bufferedTarget)
            {
                _bufferedShot = false;
                return;
            }

            if (LockFull)
            {
                _bufferedShot = false;
                return;
            }

            if (DerivedLockFill >= 1f && ShotIntervalPassed)
            {
                _bufferedShot = false;
                FireShot(true);
            }
        }

        private bool ShotIntervalPassed => NetAnchor.Elapsed(Runner, NetLastShotTick) >= _minShotInterval;

        // Spends a plate (or clicks dry with none), and freezes the focused gnome when the shot is aimed at a completed lock.
        private void FireShot(bool aimed)
        {
            var tick = Runner.Tick.Raw;
            var plates = CurrentPlates;
            NetLastShotTick = tick;
            if (plates < 1f)
            {
                NetDrySeq = (byte)(NetDrySeq + 1);
                return;
            }

            PlatesAnchor = plates - 1f;
            PlatesTick = tick;
            var hit = aimed && Target != null && DerivedLockFill >= 1f;
            NetLastShotHit = hit;
            NetShotSeq = (byte)(NetShotSeq + 1);
            if (hit)
                LockFull = true;

            if (Telemetry.Enabled)
                Telemetry.Begin("photo").Bool("hit", hit).Float("plates", PlatesAnchor).Str("mode", NetMode == ShutterMode.Auto ? "auto" : "click").Send();
        }

        /// <summary>Switches the shutter mode (variant A/B). Anyone may call it (debug panel); the camper's owner applies it.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SetShutterMode(ShutterMode mode)
        {
            if (mode == ShutterMode.Auto || mode == ShutterMode.Click)
                NetMode = mode;
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
            if (distance > Range + extraRange || Vector3.Angle(LookDirection, toPoint) > HalfAngle + extraAngle)
                return false;

            // Blocked by scenery, but not by the target itself, the camper, or items (a gnome can't hide behind its log).
            var count = Physics.RaycastNonAlloc(eye, toPoint / distance, _hits, distance, _occluders, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i].transform;
                // A gnome carried or caged by the camper is not scenery.
                var held = hit.GetComponentInParent<GnomeCapture>();
                if (held != null && held.IsCaptured)
                    continue;

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
                // The fill itself is derived from LockTick; LockFull is written only by a shot (UpdateShutter).
                return;
            }

            if (current != null)
            {
                if (Telemetry.Enabled) LogEscape(current, "lost_sight");
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
            if (Telemetry.Enabled && Target != null) LogEscape(Target, "released");
            Target = null;
            LockFull = false;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
        }

        private void ClearLock()
        {
            if (Telemetry.Enabled && Target != null) LogEscape(Target, "disabled");
            Target = null;
            LockFull = false;
            _retargetTimer = 0f;
            _lostSightTime = 0f;
        }

        // Telemetry only (camper's client): the lock on a gnome was lost before it filled, so the gnome got away.
        private void LogEscape(Petrifiable gnome, string how)
        {
            if (gnome.IsStone || LockFull || gnome.Object == null || !gnome.Object.IsValid)
                return;

            Telemetry.Begin("freeze_escape").Int("gnome", gnome.Object.StateAuthority.PlayerId).Float("lock", DerivedLockFill).Str("how", how).Send();
        }

        // The current target is kept inside the widened cone; a frozen one also survives a short moment out of sight.
        private bool TryKeep(Petrifiable target, float dt)
        {
            if (!target.IsAvailable || target.IsGazeImmune)
                return false;

            // Full lock and the gnome already turned to stone: let go so the gaze can move on to a free gnome.
            var locked = LockFull; // Set by the shot, written by this authority.
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

                // A disguised gnome can't become a new target (decided with the bool its owner replicates).
                if (candidate.Disguise != null && candidate.Disguise.IsDisguised)
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
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(HalfAngle, axis) * forward * length);
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(-HalfAngle, axis) * forward * length);
            }
        }
    }
}
