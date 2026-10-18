using System.Collections.Generic;
using BetterTogether.Audio;
using BetterTogether.Match;
using BetterTogether.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// A player that freezes while a camper keeps it locked (see <see cref="CamperGaze"/>). The camper decides who is
    /// locked; the owner of this player freezes it once the lock is full, publishes <see cref="IsPetrified"/> and lets
    /// the player break free by mashing the interact key. Every client tints a petrified player like stone.
    /// Runs after the interactor so the key press that frees the gnome doesn't also pick something up.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(100)]
    public class Petrifiable : NetworkBehaviour
    {
        [Tooltip("Heights above the feet that are checked; seeing any of them counts (e.g. feet and head).")]
        [SerializeField] private float[] _checkHeights = { 0.1f, 0.4f };
        [Tooltip("Height of the head above the feet; markers float above it.")]
        [SerializeField] private float _headHeight = 0.7f;
        [Tooltip("Warning level shown when the player is inside a camper's cone but not locked.")]
        [Range(0f, 1f)]
        [SerializeField] private float _coneWarning = 0.6f;

        [Header("Break free")]
        [Tooltip("Presses needed to break free the first time.")]
        [SerializeField] private float _basePresses = 14f;
        [Tooltip("Extra presses for every time already frozen in this match.")]
        [SerializeField] private float _pressesPerFreeze = 3f;
        [SerializeField] private float _maxPresses = 26f;
        [Tooltip("Presses lost per second once the player stops pressing.")]
        [SerializeField] private float _decayPerSecond = 3f;
        [Tooltip("Seconds without a press before the progress starts to decay.")]
        [SerializeField] private float _decayDelay = 0.4f;
        [Tooltip("Seconds after which the player is released on its own.")]
        [SerializeField] private float _maxFrozenSeconds = 6f;
        [Tooltip("Seconds the camper can't lock this player after it broke free.")]
        [SerializeField] private float _releaseImmunity = 2.5f;
        [Tooltip("Immunity after the automatic release.")]
        [SerializeField] private float _autoReleaseImmunity = 1f;
        [Tooltip("Seconds the break progress is kept after the camper loses the lock, in case it comes back.")]
        [SerializeField] private float _keepProgressSeconds = 1f;
        [Tooltip("Forward speed of the hop when released.")]
        [SerializeField] private float _hopForward = 2f;
        [Tooltip("Upward speed of the hop when released.")]
        [SerializeField] private float _hopUp = 2.5f;
        [Tooltip("Seconds without control after the hop.")]
        [SerializeField] private float _hopStun = 0.25f;

        [Header("Stone look")]
        [Tooltip("Root of the visual; every renderer below it is tinted.")]
        [SerializeField] private Transform _visual;
        [SerializeField] private Color _stoneColor = new(0.55f, 0.55f, 0.58f);
        [SerializeField] private float _stoneBlendTime = 0.2f;

        [Header("Sound")]
        [Tooltip("Tension sting when the camper locks onto you. Only you hear it; set the cue to not Spatial.")]
        [SerializeField] private SoundCue _watchedSfx;
        [Tooltip("Heard by everyone when this player turns to stone.")]
        [SerializeField] private SoundCue _petrifySfx;
        [Tooltip("Each press while frozen. Only you hear it; set the cue to not Spatial.")]
        [SerializeField] private SoundCue _pressSfx;
        [Tooltip("Heard by everyone when this player breaks free.")]
        [SerializeField] private SoundCue _releaseSfx;

        private static readonly List<Petrifiable> _all = new();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private PlayerMovement _movement;
        private InputAction _interact;
        private Renderer[] _renderers;
        private Color[] _originalColors;
        private MaterialPropertyBlock _block;
        private float _focusHeight;
        private float _stoneAmount;
        private float _frozenTime;
        private float _sinceLastPress;
        private float _keepTimer;
        private float _requiredForFreeze;
        private int _timesPetrified;
        private bool _wasLocked;
        private bool _isSpawned;

        public static Petrifiable Local { get; private set; }

        /// <summary>Every spawned petrifiable player, local and remote.</summary>
        public static IReadOnlyList<Petrifiable> All => _all;

        [Networked] public NetworkBool IsPetrified { get; private set; }
        [Networked] private float NetBreakProgress { get; set; }
        [Networked] private TickTimer GazeImmunity { get; set; }

        /// <summary>Safe to read at any time, unlike <see cref="IsPetrified"/> which needs a spawned object.</summary>
        public bool IsStone => _isSpawned && IsPetrified;

        public bool IsAvailable => _isSpawned;

        /// <summary>True while the camper cannot lock this player (just broke free or was kicked free).</summary>
        public bool IsGazeImmune => _isSpawned && !GazeImmunity.ExpiredOrNotRunning(Runner);

        /// <summary>Presses accumulated while frozen (decays when the player stops pressing).</summary>
        public float BreakProgress => _isSpawned ? NetBreakProgress : 0f;

        /// <summary>Presses needed to break free of the current freeze, or of the next one when not frozen.</summary>
        public float RequiredPresses => IsStone ? _requiredForFreeze : ComputeRequired();

        /// <summary>0..1 progress toward breaking free.</summary>
        public float BreakFraction => RequiredPresses > 0f ? Mathf.Clamp01(BreakProgress / RequiredPresses) : 0f;

        /// <summary>The camper gaze that currently locks this player, if any.</summary>
        public CamperGaze LockedBy
        {
            get
            {
                foreach (var gaze in CamperGaze.All)
                {
                    if (gaze != null && gaze.LockedTarget == this)
                        return gaze;
                }

                return null;
            }
        }

        /// <summary>Where the gaze aims for scoring: between the checked heights.</summary>
        public Vector3 FocusPoint => transform.position + Vector3.up * _focusHeight;

        /// <summary>Top of the head; markers float above it.</summary>
        public Vector3 HeadPosition => transform.position + Vector3.up * _headHeight;

        /// <summary>Owner only: a camper has this player locked but it is not frozen yet.</summary>
        public bool IsWatched { get; private set; }

        /// <summary>Owner only: this player is the camper's locked target.</summary>
        public bool IsLockTarget { get; private set; }

        /// <summary>Owner only: 0..1 warning for the HUD (lock fill, or a fixed low value inside a cone).</summary>
        public float WarningProgress { get; private set; }

        /// <summary>Owner only: total key presses counted while frozen, for HUD feedback.</summary>
        public int PressCount { get; private set; }

        /// <summary>Owner only: how many times this player was set free, for HUD feedback.</summary>
        public int ReleaseCount { get; private set; }

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _block = new MaterialPropertyBlock();
            _renderers = _visual != null ? _visual.GetComponentsInChildren<Renderer>() : new Renderer[0];
            _originalColors = new Color[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++)
            {
                var material = _renderers[i].sharedMaterial;
                _originalColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }

            var sum = 0f;
            foreach (var height in _checkHeights)
                sum += height;
            _focusHeight = _checkHeights.Length > 0 ? sum / _checkHeights.Length : 0.25f;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _all.Add(this);
            if (!HasStateAuthority)
                return;

            Local = this;
            _interact = InputSystem.actions.FindAction("Player/Interact");
            MatchManager.OnGameStarted += HandleGameStarted;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            _all.Remove(this);
            MatchManager.OnGameStarted -= HandleGameStarted;
            if (Local == this)
                Local = null;
        }

        private void HandleGameStarted() => _timesPetrified = 0;

        /// <summary>True if any checked point of this player is inside the gaze's cone (optionally widened) with a clear line.</summary>
        public bool IsVisibleTo(CamperGaze gaze, float extraAngle = 0f, float extraRange = 0f)
        {
            if (gaze == null || !_isSpawned)
                return false;

            var feet = transform.position;
            foreach (var height in _checkHeights)
            {
                if (gaze.CanSee(feet + Vector3.up * height, transform, extraAngle, extraRange))
                    return true;
            }

            return false;
        }

        /// <summary>Owner only (called by the kick): frees the statue, grants immunity and forgets the break progress.</summary>
        public void ForceRelease(float immunitySeconds)
        {
            if (!_isSpawned || !HasStateAuthority)
                return;

            if (IsPetrified)
                Release(immunitySeconds, false);
            else
                GazeImmunity = TickTimer.CreateFromSeconds(Runner, immunitySeconds);
        }

        private void Update()
        {
            if (Local != this)
                return;

            var dt = Time.deltaTime;
            var locker = LockedBy;
            var locked = locker != null;
            if (locked && !_wasLocked)
                _watchedSfx.Play(); // Only this gnome hears it (2D), like the HUD warning.

            _wasLocked = locked;
            IsLockTarget = locked;

            var immune = IsGazeImmune;
            var shouldFreeze = locked && locker.LockProgress >= 1f && !immune;
            if (shouldFreeze && !IsPetrified)
                BeginFreeze();
            else if (!shouldFreeze && IsPetrified)
                LoseFreeze();

            if (IsPetrified)
                UpdateBreaking(dt);
            else
                UpdateIdle(dt);

            _movement.IsFrozen = IsPetrified;
            IsWatched = locked && !IsPetrified;
            WarningProgress = ComputeWarning(locker, immune);
        }

        private void BeginFreeze()
        {
            if (_keepTimer > 0f)
            {
                _keepTimer = 0f; // The camper came back in time: resume where it was.
            }
            else
            {
                _requiredForFreeze = ComputeRequired();
                _timesPetrified++;
                _frozenTime = 0f;
                NetBreakProgress = 0f;
            }

            _sinceLastPress = 0f;
            IsPetrified = true;
            _petrifySfx.PlayAt(transform.position);
        }

        // The camper stopped looking: unfreeze without immunity or hop, keeping the progress for a moment.
        private void LoseFreeze()
        {
            IsPetrified = false;
            _keepTimer = _keepProgressSeconds;
        }

        private void UpdateIdle(float dt)
        {
            if (_keepTimer <= 0f)
                return;

            _keepTimer -= dt;
            if (_keepTimer <= 0f)
                NetBreakProgress = 0f;
        }

        private void UpdateBreaking(float dt)
        {
            _frozenTime += dt;
            _sinceLastPress += dt;

            if (_interact != null && _interact.WasPressedThisFrame())
            {
                NetBreakProgress += 1f;
                _sinceLastPress = 0f;
                PressCount++;
                _pressSfx.Play();
            }
            else if (_sinceLastPress > _decayDelay)
            {
                NetBreakProgress = Mathf.Max(0f, NetBreakProgress - _decayPerSecond * dt);
            }

            if (NetBreakProgress >= _requiredForFreeze)
                Release(_releaseImmunity, true);
            else if (_frozenTime >= _maxFrozenSeconds)
                Release(_autoReleaseImmunity, true);
        }

        private void Release(float immunitySeconds, bool hop)
        {
            IsPetrified = false;
            GazeImmunity = TickTimer.CreateFromSeconds(Runner, immunitySeconds);
            NetBreakProgress = 0f;
            _keepTimer = 0f;
            _frozenTime = 0f;
            _movement.IsFrozen = false;
            ReleaseCount++;
            _releaseSfx.PlayAt(transform.position);

            if (hop)
                Hop();
        }

        private void Hop()
        {
            var forward = FirstPersonCamera.Instance != null ? FirstPersonCamera.Instance.transform.forward : transform.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            _movement.ApplyKnockback(forward * _hopForward + Vector3.up * _hopUp, _hopStun);
        }

        private float ComputeRequired() => Mathf.Min(_maxPresses, _basePresses + _pressesPerFreeze * _timesPetrified);

        private float ComputeWarning(CamperGaze locker, bool immune)
        {
            if (IsPetrified || immune)
                return 0f;

            if (locker != null)
                return Mathf.Clamp01(locker.LockProgress);

            foreach (var gaze in CamperGaze.All)
            {
                if (IsVisibleTo(gaze))
                    return _coneWarning;
            }

            return 0f;
        }

        public override void Render()
        {
            var target = IsPetrified ? 1f : 0f;
            if (Mathf.Approximately(_stoneAmount, target))
                return;

            var step = _stoneBlendTime > 0f ? Time.deltaTime / _stoneBlendTime : 1f;
            _stoneAmount = Mathf.MoveTowards(_stoneAmount, target, step);
            ApplyStone();
        }

        private void ApplyStone()
        {
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _block.SetColor(BaseColorId, Color.Lerp(_originalColors[i], _stoneColor, _stoneAmount));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
