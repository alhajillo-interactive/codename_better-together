using BetterTogether.Combat;
using BetterTogether.Diagnostics;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// Garden-gnome disguise. A gnome that stays crouched and still for a moment turns into a painted ceramic ornament:
    /// the camper's gaze can't pick it as a NEW lock target while it is disguised (a lock that already exists stays).
    /// Moving, standing up, interacting, carrying, riding, being petrified or being kicked ends it.
    /// Shared mode: only the owner writes <see cref="Disguised"/> (in FixedUpdateNetwork); the camper's gaze decides with
    /// that replicated bool, and every client presents the tint and the frozen pose in Render.
    /// Late joiner: Render snaps to the replicated state. Master change: irrelevant, the player owns the state.
    /// Player leaves: the object despawns and the gaze never sees it again.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class GardenDisguise : NetworkBehaviour
    {
        [Header("Rules")]
        [Tooltip("Seconds the gnome must stay crouched and still before it disguises.")]
        [SerializeField] private float _holdStill = 1f;
        [Tooltip("Speed (m/s) under which the gnome counts as still.")]
        [SerializeField] private float _stillSpeed = 0.1f;
        [Tooltip("Seconds after the disguise ends before the gnome can disguise again.")]
        [SerializeField] private float _cooldown = 3f;

        [Header("Look")]
        [Tooltip("Root of the visual; every renderer below it is tinted. Empty = the Animator's object.")]
        [SerializeField] private Transform _visual;
        [Tooltip("Glossy painted-ceramic tint (different from the grey stone of the petrify).")]
        [SerializeField] private Color _ceramicColor = new(0.85f, 0.45f, 0.35f);
        [Tooltip("Seconds of the tint transition in both directions.")]
        [SerializeField] private float _tintTime = 0.2f;
        [Tooltip("Seconds after the tint is complete before the Animator freezes (lets the crouch pose settle).")]
        [SerializeField] private float _freezeDelay = 0.3f;
        [Tooltip("Scale overshoot of the pop when the disguise drops (0.1 = +10%).")]
        [SerializeField] private float _popScale = 0.12f;
        [Tooltip("Seconds of the reveal pop.")]
        [SerializeField] private float _popTime = 0.25f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private PlayerMovement _movement;
        private Petrifiable _petrifiable;
        private Kickable _kickable;
        private GnomeStack _stack;
        private PlayerInteractor _interactor;
        private InputAction _interact;
        private Animator _animator;
        private Renderer[] _renderers;
        private Color[] _originalColors;
        private MaterialPropertyBlock _block;
        private bool _isSpawned;

        // Owner state (FixedUpdateNetwork only).
        private float _stillTime;
        private float _cooldownLeft;
        private float _disguisedFor;
        private bool _wasStunned;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        // Presentation state (Render only).
        private bool _snapped;
        private bool _wasDisguised;
        private float _tintAmount;
        private float _disguisedSince;
        private bool _animatorFrozen;
        private float _popLeft;
        private Vector3 _scaleRest;
        private Transform _popTarget;

        /// <summary>Written only by the owner; read through <see cref="IsDisguised"/>.</summary>
        [Networked] private NetworkBool Disguised { get; set; }

        /// <summary>True while this gnome looks like an ornament. Safe to read at any time.</summary>
        public bool IsDisguised => _isSpawned && Disguised;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _petrifiable = GetComponent<Petrifiable>();
            _kickable = GetComponent<Kickable>();
            _stack = GetComponent<GnomeStack>();
            _interactor = GetComponent<PlayerInteractor>();
            _animator = GetComponentInChildren<Animator>();
            _block = new MaterialPropertyBlock();
            if (_visual == null && _animator != null)
                _visual = _animator.transform;

            _popTarget = _visual;
            if (_popTarget != null)
                _scaleRest = _popTarget.localScale;

            _renderers = _visual != null ? _visual.GetComponentsInChildren<Renderer>() : new Renderer[0];
            _originalColors = new Color[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++)
            {
                var material = _renderers[i].sharedMaterial;
                _originalColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _snapped = false;
            _hasLastPosition = false;
            if (!HasStateAuthority)
                return;

            _interact = InputSystem.actions.FindAction("Player/Interact");
            _stillTime = 0f;
            _cooldownLeft = 0f;
            Disguised = false;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isSpawned = false;
            RestoreAnimator();
            if (_popTarget != null)
                _popTarget.localScale = _scaleRest;
        }

        private void OnDisable() => RestoreAnimator();

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _petrifiable == null)
                return;

            var dt = Runner.DeltaTime;
            var position = transform.position;
            var speed = _hasLastPosition && dt > 0f ? (position - _lastPosition).magnitude / dt : float.MaxValue;
            _lastPosition = position;
            _hasLastPosition = true;

            var stunned = _kickable != null && _kickable.IsStunned;
            var kicked = stunned && !_wasStunned;
            _wasStunned = stunned;

            if (_cooldownLeft > 0f)
                _cooldownLeft -= dt;

            if (Disguised)
            {
                _disguisedFor += dt;
                var reason = BreakReason(speed, stunned, kicked);
                if (reason != null)
                    EndDisguise(reason);
                return;
            }

            var eligible = _cooldownLeft <= 0f && speed < _stillSpeed && CanKeepDisguise(stunned);
            _stillTime = eligible ? _stillTime + dt : 0f;
            if (_stillTime < _holdStill)
                return;

            _stillTime = 0f;
            _disguisedFor = 0f;
            Disguised = true;
            if (Telemetry.Enabled)
                Telemetry.Begin("disguise_start", this).Send();
        }

        // True while nothing forbids being disguised (crouched, empty hands, alone, free, not stunned).
        private bool CanKeepDisguise(bool stunned)
        {
            if (stunned || !_movement.IsCrouching || _petrifiable.IsStone)
                return false;

            if (_stack != null && (_stack.IsRiding || _stack.Rider != null))
                return false;

            return _interactor == null || _interactor.Carried == null;
        }

        // Why the disguise must end now; null if it can continue.
        private string BreakReason(float speed, bool stunned, bool kicked)
        {
            if (kicked)
                return "kicked";
            if (stunned)
                return "stunned";
            if (_petrifiable.IsStone)
                return "petrified";
            if (_interact != null && _interact.IsPressed())
                return "interact";
            if (!_movement.IsCrouching)
                return "stand";
            if (speed >= _stillSpeed)
                return "moved";
            if (!CanKeepDisguise(false))
                return "busy";

            return null;
        }

        private void EndDisguise(string reason)
        {
            Disguised = false;
            _cooldownLeft = _cooldown;
            _stillTime = 0f;
            if (!Telemetry.Enabled)
                return;

            Telemetry.Begin("disguise_end", this).Float("duration", _disguisedFor).Str("reason", reason).Send();
            if (reason == "kicked")
                Telemetry.Begin("disguise_revealed", this).Float("duration", _disguisedFor).Send();
        }

        public override void Render()
        {
            if (!_isSpawned)
                return;

            var disguised = Disguised;
            var target = disguised ? 1f : 0f;
            if (!_snapped)
            {
                // Late joiner: no transition and no pop for a disguise that already existed.
                _snapped = true;
                _wasDisguised = disguised;
                _tintAmount = target;
                _disguisedSince = Time.time - _freezeDelay - _tintTime;
                ApplyTint();
                return;
            }

            if (disguised != _wasDisguised)
            {
                if (disguised)
                {
                    _disguisedSince = Time.time;
                }
                else
                {
                    RestoreAnimator();
                    _popLeft = _popTime;
                }

                _wasDisguised = disguised;
            }

            if (!Mathf.Approximately(_tintAmount, target))
            {
                var step = _tintTime > 0f ? Time.deltaTime / _tintTime : 1f;
                _tintAmount = Mathf.MoveTowards(_tintAmount, target, step);
                ApplyTint();
            }

            if (disguised && !_animatorFrozen && _animator != null && Time.time - _disguisedSince >= _tintTime + _freezeDelay)
            {
                _animator.speed = 0f;
                _animatorFrozen = true;
            }

            AnimatePop();
        }

        // Soft scale pop after the disguise drops; skipped while the kick wobble owns the body's scale.
        private void AnimatePop()
        {
            if (_popLeft <= 0f || _popTarget == null)
                return;

            _popLeft -= Time.deltaTime;
            if (_kickable != null && _kickable.IsStunned)
            {
                _popLeft = 0f;
                return;
            }

            if (_popLeft <= 0f)
            {
                _popTarget.localScale = _scaleRest;
                return;
            }

            var t = 1f - _popLeft / Mathf.Max(_popTime, 0.01f);
            _popTarget.localScale = _scaleRest * (1f + Mathf.Sin(t * Mathf.PI) * _popScale);
        }

        private void RestoreAnimator()
        {
            if (!_animatorFrozen)
                return;

            _animatorFrozen = false;
            if (_animator != null)
                _animator.speed = 1f;
        }

        // Stone owns the tint while the gnome is petrified; the disguise never fights it.
        private void ApplyTint()
        {
            if (_petrifiable != null && _petrifiable.IsStone)
                return;

            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _block.SetColor(BaseColorId, Color.Lerp(_originalColors[i], _ceramicColor, _tintAmount));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
