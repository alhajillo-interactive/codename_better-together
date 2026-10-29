using System.Collections.Generic;
using BetterTogether.Diagnostics;
using BetterTogether.Player;
using Fusion;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace BetterTogether.Combat
{
    /// <summary>
    /// The camper's kick: pressing Attack launches every <see cref="Kickable"/> inside a cone in front of the camper.
    /// Runs only on the camper's own client; the hit is delivered to each gnome's owner through <see cref="Kickable.Kick"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class CamperKick : NetworkBehaviour
    {
        [Header("Range")]
        [SerializeField] private float _range = 1.6f;
        [Tooltip("Half-angle of the cone in front of the camper, in degrees.")]
        [Range(0f, 180f)]
        [SerializeField] private float _halfAngle = 50f;
        [SerializeField] private LayerMask _targetMask = ~0;

        [Header("Force")]
        [Tooltip("With the defaults a gnome lands about 2.5 m away after a ~0.3 m hop.")]
        [SerializeField] private float _horizontalSpeed = 5f;
        [SerializeField] private float _upwardSpeed = 3.5f;
        [Tooltip("Delay after a kick that landed.")]
        [FormerlySerializedAs("_cooldown")]
        [SerializeField] private float _hitCooldown = 0.8f;
        [Tooltip("Delay after a kick that hit nothing; longer, so spamming is punished.")]
        [SerializeField] private float _missCooldown = 1.2f;

        [Header("Recoil")]
        [Tooltip("Impulse played on the camper's own camera when kicking.")]
        [SerializeField] private CinemachineImpulseSource _kickRecoil;
        [SerializeField] private float _recoilForce = 0.4f;
        [SerializeField] private float _missRecoilForce = 0.1f;
        [Tooltip("Field of view added for a moment when the kick lands.")]
        [SerializeField] private float _hitFovPunch = 4f;
        [SerializeField] private float _hitFovPunchTime = 0.25f;

        [Header("Feel (local presentation only)")]
        [Tooltip("Tiny impulse on the camper's camera the instant the kick starts: the camera sinks back and down. The kick itself is not delayed.")]
        [SerializeField] private float _anticipationForce = 0.05f;
        [Tooltip("Seconds the kicked gnome's Animator freezes on impact (visual hit stop, never Time.timeScale).")]
        [SerializeField] private float _hitStopTime = 0.06f;
        [Tooltip("Field of view change when the kick hits nothing (negative = a small tug in).")]
        [SerializeField] private float _missFovPunch = -1.5f;
        [SerializeField] private float _missFovPunchTime = 0.2f;

        private readonly Collider[] _overlap = new Collider[16];
        private readonly List<Kickable> _targets = new();
        private InputAction _attack;
        private PlayerMovement _movement;
        private float _readyTime;
        private float _pendingFovPunchTime = -1f;

        public static CamperKick Local { get; private set; }

        /// <summary>Only meaningful on the local camper.</summary>
        public bool HasTargetInRange => _targets.Count > 0;
        public bool IsReady => Time.time >= _readyTime;

        private void Awake() => _movement = GetComponent<PlayerMovement>();

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            Local = this;
            _attack = InputSystem.actions.FindAction("Player/Attack");
            if (!TryGetComponent<KickReadout>(out _))
                gameObject.AddComponent<KickReadout>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (Local != this)
                return;

            FindTargets();

            // The hit FOV punch waits for the visual hit stop; plain float compare, no allocations.
            if (_pendingFovPunchTime >= 0f && Time.unscaledTime >= _pendingFovPunchTime)
            {
                _pendingFovPunchTime = -1f;
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(_hitFovPunch, _hitFovPunchTime);
            }

            // Left click also re-locks the cursor (FirstPersonCamera), so ignore it while the cursor is free.
            if (_attack != null && _attack.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked && IsReady)
                Kick();
        }

        private void Kick()
        {
            var hit = _targets.Count > 0;
            _readyTime = Time.time + (hit ? _hitCooldown : _missCooldown);
            _movement.PlayKickAnimation();
            if (Telemetry.Enabled) LogKick(hit);

            // Anticipation cue (local): the logic below runs this same frame.
            if (_kickRecoil != null && _anticipationForce > 0f)
                _kickRecoil.GenerateImpulseWithVelocity((Vector3.down + Vector3.back * 0.5f).normalized * _anticipationForce);

            var forward = Flat(transform.forward).normalized;
            foreach (var target in _targets)
            {
                // Mostly where the camper faces, bent toward the target so side hits push sideways.
                var toTarget = Flat(target.transform.position - transform.position).normalized;
                var direction = (forward + toTarget).normalized;
                target.Kick(direction * _horizontalSpeed + Vector3.up * _upwardSpeed, transform.position);
                target.PlayHitStop(_hitStopTime);
            }

            if (_kickRecoil != null)
                _kickRecoil.GenerateImpulseWithForce(hit ? _recoilForce : _missRecoilForce);

            if (hit)
                _pendingFovPunchTime = Time.unscaledTime + _hitStopTime;
            else if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(_missFovPunch, _missFovPunchTime); // The whoosh is _kickSwingSfx, played by PlayKickAnimation.
        }

        // Telemetry only: whether the kick landed, how many gnomes it reached and the distance to the nearest one.
        private void LogKick(bool hit)
        {
            Kickable nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var target in _targets)
            {
                var distance = Flat(target.transform.position - transform.position).magnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = target;
                }
            }

            // A miss still reports how far the nearest gnome was (no allocations: indexed loop over the cached list).
            if (nearest == null)
            {
                var all = PlayerCharacter.All;
                for (var i = 0; i < all.Count; i++)
                {
                    var player = all[i];
                    if (player == null || player.Role != PlayerRole.Gnome)
                        continue;

                    var distance = Flat(player.transform.position - transform.position).magnitude;
                    if (distance < nearestDistance)
                        nearestDistance = distance;
                }
            }

            var found = nearestDistance < float.MaxValue;
            var targetId = nearest != null && nearest.Object != null && nearest.Object.IsValid ? nearest.Object.StateAuthority.PlayerId : -1;
            Telemetry.Begin("kick").Bool("hit", hit).Int("targets", _targets.Count).Int("target", targetId)
                .Float("distance", found ? nearestDistance : -1f).Send();
        }

        private void FindTargets()
        {
            _targets.Clear();

            var origin = transform.position;
            var forward = Flat(transform.forward);
            var count = Physics.OverlapSphereNonAlloc(origin + Vector3.up * 0.5f, _range, _overlap, _targetMask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var kickable = _overlap[i].GetComponentInParent<Kickable>();
                if (kickable == null || !kickable.CanBeKicked || kickable.transform == transform || _targets.Contains(kickable))
                    continue;

                var toTarget = Flat(kickable.transform.position - origin);
                if (toTarget.magnitude <= _range && Vector3.Angle(forward, toTarget) <= _halfAngle)
                    _targets.Add(kickable);
            }
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private void OnDrawGizmosSelected()
        {
            var origin = transform.position + Vector3.up * 0.05f;
            var forward = Flat(transform.forward).normalized;
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.8f);
            Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, -_halfAngle, 0f) * forward * _range);
            Gizmos.DrawLine(origin, origin + Quaternion.Euler(0f, _halfAngle, 0f) * forward * _range);
            Gizmos.DrawLine(origin, origin + forward * _range);
        }
    }
}
