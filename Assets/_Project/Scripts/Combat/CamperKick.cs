using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Combat
{
    /// <summary>
    /// The camper's kick: pressing Attack launches every <see cref="Kickable"/> inside a cone in front of the camper.
    /// Runs only on the camper's own client; the hit is delivered to each gnome's owner through <see cref="Kickable.Kick"/>.
    /// </summary>
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
        [SerializeField] private float _cooldown = 0.8f;

        private readonly Collider[] _overlap = new Collider[16];
        private readonly List<Kickable> _targets = new();
        private InputAction _attack;
        private float _readyTime;

        public static CamperKick Local { get; private set; }

        /// <summary>Only meaningful on the local camper.</summary>
        public bool HasTargetInRange => _targets.Count > 0;
        public bool IsReady => Time.time >= _readyTime;

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            Local = this;
            _attack = InputSystem.actions.FindAction("Player/Attack");
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

            // Left click also re-locks the cursor (FirstPersonCamera), so ignore it while the cursor is free.
            if (_attack != null && _attack.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked && IsReady)
                Kick();
        }

        private void Kick()
        {
            _readyTime = Time.time + _cooldown;

            var forward = Flat(transform.forward).normalized;
            foreach (var target in _targets)
            {
                // Mostly where the camper faces, bent toward the target so side hits push sideways.
                var toTarget = Flat(target.transform.position - transform.position).normalized;
                var direction = (forward + toTarget).normalized;
                target.Kick(direction * _horizontalSpeed + Vector3.up * _upwardSpeed, transform.position);
            }
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
