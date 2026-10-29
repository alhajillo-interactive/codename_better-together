using BetterTogether.Audio;
using BetterTogether.Diagnostics;
using BetterTogether.Fireplace;
using BetterTogether.Gaze;
using BetterTogether.Interaction;
using BetterTogether.Player;
using BetterTogether.UI;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;

namespace BetterTogether.Combat
{
    /// <summary>
    /// Lets a player be kicked. Shared mode: the kicker doesn't own this player, so the kick travels as an RPC
    /// to the owner, who launches its own movement, drops whatever it was carrying and gets stunned.
    /// The stun is networked so every client shows the dizzy effect.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class Kickable : NetworkBehaviour
    {
        [Tooltip("Upper bound for the kick speed, so a modified client can't send players to orbit.")]
        [SerializeField] private float _maxKickSpeed = 8f;
        [Tooltip("Seconds without movement control after being kicked.")]
        [SerializeField] private float _stunDuration = 1.5f;
        [Tooltip("Ignore kicks while still stunned, so a gnome can't be kicked again and again.")]
        [SerializeField] private bool _immuneWhileStunned = true;
        [Tooltip("Extra seconds without being kickable once the stun has ended.")]
        [SerializeField] private float _immunityAfterStun = 3f;
        [Tooltip("Where the dropped item lands between this player (0) and the kicker (1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _dropTowardKicker = 0.5f;

        [Tooltip("A kick on a petrified player breaks the stone and grants it a short immunity to the gaze.")]
        [SerializeField] private bool _kickFreesStone = true;

        [Header("Camera shake")]
        [Tooltip("Impulse played on this player's own camera when kicked.")]
        [SerializeField] private CinemachineImpulseSource _kickShake;
        [SerializeField] private float _kickShakeForce = 0.6f;
        [Tooltip("Roll of the camera when kicked, flipped depending on the side the kick came from.")]
        [SerializeField] private float _kickRollDegrees = 15f;
        [SerializeField] private float _kickRollTime = 0.4f;
        [SerializeField] private Color _kickFlashColor = new(1f, 0.1f, 0.1f, 0.3f);
        [SerializeField] private float _kickFlashTime = 0.3f;

        [Header("Sound")]
        [Tooltip("Hit when this player gets kicked. Heard by everyone, from where it happens.")]
        [SerializeField] private SoundCue _kickedSfx;

        [Header("Dizzy effect")]
        [Tooltip("Visual-only child that wobbles while stunned (not the root, which carries the collider).")]
        [SerializeField] private Transform _wobbleTarget;
        [SerializeField] private float _wobbleAngle = 12f;
        [Tooltip("Height of the orbiting stars above the player's feet.")]
        [SerializeField] private float _starsHeight = 0.55f;
        [SerializeField] private float _starsRadius = 0.15f;
        [SerializeField] private float _starSize = 0.05f;
        [SerializeField] private Color _starColor = new(1f, 0.85f, 0.2f);

        [Tooltip("Optional particle system of stars over the head (built by GnoMore/Construir feel de patada). Empty = the code-built star ring.")]
        [SerializeField] private ParticleSystem _stunStars;

        [Header("Hit reaction")]
        [Tooltip("Seconds at the start of the stun the visual spends spinning around Y.")]
        [SerializeField] private float _spinTime = 0.45f;
        [SerializeField] private float _spinTurns = 2f;
        [Tooltip("Squash right after the kick: Y scale, and XZ scale.")]
        [SerializeField] private Vector2 _squashScale = new(0.6f, 1.3f);
        [SerializeField] private float _squashTime = 0.25f;

        private const int StarCount = 3;

        private PlayerMovement _movement;
        private PlayerInteractor _interactor;
        private Petrifiable _petrifiable;
        private Transform _stars;
        private Quaternion _wobbleRest;
        private Vector3 _scaleRest;
        private float _stunStartTime;
        private bool _wasStunned;
        private bool _showingDizzy;
        private bool _isSpawned;
        private Animator _hitStopAnimator;
        private float _hitStopUntil = -1f;

        [Networked] private TickTimer StunTimer { get; set; }
        [Networked] private TickTimer ProtectedTimer { get; set; }

        // Other clients query these every frame, also before Spawned / after Despawned, when networked state can't be read.
        public bool IsStunned => _isSpawned && !StunTimer.ExpiredOrNotRunning(Runner);
        /// <summary>True while the local visual hit stop holds the Animator at speed 0.</summary>
        public bool IsHitStopping => _hitStopUntil >= 0f;
        public bool CanBeKicked => _isSpawned && ProtectedTimer.ExpiredOrNotRunning(Runner);

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _interactor = GetComponent<PlayerInteractor>();
            _petrifiable = GetComponent<Petrifiable>();
            _hitStopAnimator = GetComponentInChildren<Animator>();
            if (_wobbleTarget == null)
                return;

            _wobbleRest = _wobbleTarget.localRotation;
            _scaleRest = _wobbleTarget.localScale;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _stars = _stunStars == null ? CreateStars() : null;
        }

        /// <summary>
        /// Local visual hit stop: freezes this player's Animator for a moment (never Time.timeScale, the simulation keeps running).
        /// Called by the kicker's client on its own copy; each client decides alone.
        /// </summary>
        public void PlayHitStop(float seconds)
        {
            if (_hitStopAnimator == null || seconds <= 0f)
                return;

            _hitStopAnimator.speed = 0f;
            _hitStopUntil = Time.unscaledTime + seconds;
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        private void OnDisable()
        {
            _hitStopUntil = -1f;
            if (_hitStopAnimator != null)
                _hitStopAnimator.speed = 1f;
        }

        /// <summary>Called by the kicker's client on its local copy of this player.</summary>
        public void Kick(Vector3 velocity, Vector3 kickerPosition) => RPC_Kick(velocity, kickerPosition);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Kick(Vector3 velocity, Vector3 kickerPosition)
        {
            if (!CanBeKicked)
                return;

            StunTimer = TickTimer.CreateFromSeconds(Runner, _stunDuration);
            var protectedFor = (_immuneWhileStunned ? _stunDuration : 0f) + _immunityAfterStun;
            ProtectedTimer = TickTimer.CreateFromSeconds(Runner, protectedFor);
            if (Telemetry.Enabled) LogStunStart();
            DropCarried(kickerPosition);
            if (_kickFreesStone && _petrifiable != null && _petrifiable.IsStone)
                _petrifiable.ForceRelease(_stunDuration + 1f);

            _movement.ApplyKnockback(Vector3.ClampMagnitude(velocity, _maxKickSpeed), _stunDuration);

            // Runs on the kicked player's own client, so only their camera shakes.
            if (_kickShake != null)
                _kickShake.GenerateImpulseWithVelocity(velocity.normalized * _kickShakeForce);

            PlayHitFeedback(kickerPosition);
        }

        // Telemetry only. Runs on the kicked player's own client (the state authority of this player), before the item is dropped.
        private void LogStunStart()
        {
            var carried = _interactor != null ? _interactor.Carried : null;
            Telemetry.Begin("stun_start").Float("duration", _stunDuration).Bool("dropped", carried != null)
                .Str("object", carried != null ? carried.DisplayName : "none").Send();
        }

        private void PlayHitFeedback(Vector3 kickerPosition)
        {
            if (CameraPunch.Instance != null)
            {
                var fromKicker = Vector3.ProjectOnPlane(transform.position - kickerPosition, Vector3.up);
                var side = Vector3.Dot(fromKicker, CameraPunch.Instance.transform.right) >= 0f ? 1f : -1f;
                CameraPunch.Instance.PunchRoll(side * _kickRollDegrees, _kickRollTime);
            }

            if (ScreenFlash.Instance != null)
                ScreenFlash.Instance.Flash(_kickFlashColor, _kickFlashTime);
        }

        private void DropCarried(Vector3 kickerPosition)
        {
            var carried = _interactor != null ? _interactor.Carried : null;
            if (carried == null)
                return;

            // A kicked bucket spills; the release below then drops it empty.
            if (carried.TryGetComponent<WaterBucket>(out var bucket))
                bucket.RequestSpill(_interactor);

            var position = transform.position;
            var toKicker = Vector3.ProjectOnPlane(kickerPosition - position, Vector3.up);
            carried.RequestRelease(_interactor, position + toKicker * _dropTowardKicker);
        }

        public override void Render()
        {
            if (_hitStopUntil >= 0f && Time.unscaledTime >= _hitStopUntil)
            {
                _hitStopUntil = -1f;
                if (_hitStopAnimator != null)
                    _hitStopAnimator.speed = 1f;
            }

            var stunned = IsStunned;
            if (stunned && !_wasStunned)
                _stunStartTime = Time.time;
            if (Telemetry.Enabled && !stunned && _wasStunned && HasStateAuthority)
                Telemetry.Begin("stun_end").Float("duration", Time.time - _stunStartTime).Send();
            _wasStunned = stunned;

            if (stunned)
            {
                if (_stars != null)
                    _stars.localRotation = Quaternion.Euler(0f, Time.time * 240f, 0f);
                AnimateBody(Time.time - _stunStartTime);
            }

            if (stunned == _showingDizzy)
                return;

            _showingDizzy = stunned;
            if (_stars != null)
                _stars.gameObject.SetActive(stunned);
            if (_stunStars != null)
            {
                if (stunned)
                    _stunStars.Play();
                else
                    _stunStars.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            // The stun is networked, so this runs on every client: everyone hears the hit.
            if (stunned)
                _kickedSfx.PlayAt(transform.position);
            if (!stunned && _wobbleTarget != null)
            {
                _wobbleTarget.localRotation = _wobbleRest;
                _wobbleTarget.localScale = _scaleRest;
            }
        }

        /// <summary>Spin and squash right after the kick, then the dizzy wobble.</summary>
        private void AnimateBody(float sinceStun)
        {
            if (_wobbleTarget == null)
                return;

            var t = Time.time;
            var spin = 0f;
            if (sinceStun < _spinTime)
            {
                var remaining = 1f - sinceStun / _spinTime;
                spin = 360f * _spinTurns * (1f - remaining * remaining);
            }

            _wobbleTarget.localRotation = _wobbleRest * Quaternion.Euler(0f, spin, 0f) * Quaternion.Euler(
                Mathf.Sin(t * 9f) * _wobbleAngle, 0f, Mathf.Cos(t * 7f) * _wobbleAngle);

            if (sinceStun >= _squashTime)
            {
                _wobbleTarget.localScale = _scaleRest;
                return;
            }

            var back = 1f - (1f - sinceStun / _squashTime) * (1f - sinceStun / _squashTime);
            var y = Mathf.Lerp(_squashScale.x, 1f, back);
            var xz = Mathf.Lerp(_squashScale.y, 1f, back);
            _wobbleTarget.localScale = Vector3.Scale(_scaleRest, new Vector3(xz, y, xz));
        }

        /// <summary>Small ring of glowing spheres above the head; built in code so no art asset is needed yet.</summary>
        private Transform CreateStars()
        {
            var ring = new GameObject("DizzyStars").transform;
            ring.SetParent(transform, false);
            ring.localPosition = Vector3.up * _starsHeight;

            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", _starColor);
            block.SetColor("_EmissionColor", _starColor);

            for (var i = 0; i < StarCount; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(star.GetComponent<Collider>()); // Must not block interaction raycasts or kicks.
                star.name = "Star";
                star.transform.SetParent(ring, false);
                var angle = i * Mathf.PI * 2f / StarCount;
                star.transform.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _starsRadius;
                star.transform.localScale = Vector3.one * _starSize;

                var starRenderer = star.GetComponent<Renderer>();
                starRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                starRenderer.SetPropertyBlock(block);
            }

            ring.gameObject.SetActive(false);
            return ring;
        }
    }
}
