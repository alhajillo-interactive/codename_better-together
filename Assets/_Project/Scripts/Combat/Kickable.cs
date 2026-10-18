using BetterTogether.Audio;
using BetterTogether.Interaction;
using BetterTogether.Player;
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
        [SerializeField] private float _stunDuration = 2f;
        [Tooltip("Ignore kicks while still stunned, so a gnome can't be kicked again and again.")]
        [SerializeField] private bool _immuneWhileStunned = true;
        [Tooltip("Where the dropped item lands between this player (0) and the kicker (1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _dropTowardKicker = 0.5f;

        [Header("Camera shake")]
        [Tooltip("Impulse played on this player's own camera when kicked.")]
        [SerializeField] private CinemachineImpulseSource _kickShake;
        [SerializeField] private float _kickShakeForce = 0.6f;

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

        private const int StarCount = 3;

        private PlayerMovement _movement;
        private PlayerInteractor _interactor;
        private Transform _stars;
        private Quaternion _wobbleRest;
        private bool _showingDizzy;
        private bool _isSpawned;

        [Networked] private TickTimer StunTimer { get; set; }

        // Other clients query these every frame, also before Spawned / after Despawned, when networked state can't be read.
        public bool IsStunned => _isSpawned && !StunTimer.ExpiredOrNotRunning(Runner);
        public bool CanBeKicked => _isSpawned && !(_immuneWhileStunned && IsStunned);

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            _interactor = GetComponent<PlayerInteractor>();
            if (_wobbleTarget != null)
                _wobbleRest = _wobbleTarget.localRotation;
        }

        public override void Spawned()
        {
            _isSpawned = true;
            _stars = CreateStars();
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        /// <summary>Called by the kicker's client on its local copy of this player.</summary>
        public void Kick(Vector3 velocity, Vector3 kickerPosition) => RPC_Kick(velocity, kickerPosition);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_Kick(Vector3 velocity, Vector3 kickerPosition)
        {
            if (!CanBeKicked)
                return;

            StunTimer = TickTimer.CreateFromSeconds(Runner, _stunDuration);
            DropCarried(kickerPosition);
            _movement.ApplyKnockback(Vector3.ClampMagnitude(velocity, _maxKickSpeed), _stunDuration);

            // Runs on the kicked player's own client, so only their camera shakes.
            if (_kickShake != null)
                _kickShake.GenerateImpulseWithVelocity(velocity.normalized * _kickShakeForce);
        }

        private void DropCarried(Vector3 kickerPosition)
        {
            var carried = _interactor != null ? _interactor.Carried : null;
            if (carried == null)
                return;

            var position = transform.position;
            var toKicker = Vector3.ProjectOnPlane(kickerPosition - position, Vector3.up);
            carried.RequestRelease(_interactor, position + toKicker * _dropTowardKicker);
        }

        public override void Render()
        {
            var stunned = IsStunned;
            if (stunned)
            {
                var t = Time.time;
                _stars.localRotation = Quaternion.Euler(0f, t * 240f, 0f);
                if (_wobbleTarget != null)
                    _wobbleTarget.localRotation = _wobbleRest * Quaternion.Euler(
                        Mathf.Sin(t * 9f) * _wobbleAngle, 0f, Mathf.Cos(t * 7f) * _wobbleAngle);
            }

            if (stunned == _showingDizzy)
                return;

            _showingDizzy = stunned;
            _stars.gameObject.SetActive(stunned);
            // The stun is networked, so this runs on every client: everyone hears the hit.
            if (stunned)
                _kickedSfx.PlayAt(transform.position);
            if (!stunned && _wobbleTarget != null)
                _wobbleTarget.localRotation = _wobbleRest;
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
