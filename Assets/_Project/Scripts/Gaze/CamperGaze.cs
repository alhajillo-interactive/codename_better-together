using System.Collections.Generic;
using BetterTogether.Interaction;
using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Gaze
{
    /// <summary>
    /// The camper's petrifying gaze: any <see cref="Petrifiable"/> inside the central cone of the camper's view,
    /// within range and with a clear line of sight, is frozen in place. The screen edges stay outside the cone,
    /// so gnomes can still move in the camper's peripheral vision.
    /// The camper only publishes where it looks; each gnome's owner runs <see cref="CanSee"/> on its own copy.
    /// </summary>
    public class CamperGaze : NetworkBehaviour
    {
        [Tooltip("Eye position; use the camera anchor.")]
        [SerializeField] private Transform _eye;
        [Tooltip("Half-angle of the petrifying cone. The camera sees ~45° to each side, so 30° leaves the edges free.")]
        [Range(1f, 90f)]
        [SerializeField] private float _halfAngle = 30f;
        [SerializeField] private float _range = 20f;
        [Tooltip("Layers that block the gaze (walls, tents, trees...).")]
        [SerializeField] private LayerMask _occluders = ~0;
        [SerializeField] private bool _enabledOnSpawn = true;

        private static readonly List<CamperGaze> _all = new();
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Every spawned camper gaze, local and remote.</summary>
        public static IReadOnlyList<CamperGaze> All => _all;

        [Networked] private float Pitch { get; set; }
        [Networked] public NetworkBool IsGazeEnabled { get; private set; }

        private Vector3 EyePosition => _eye != null ? _eye.position : transform.position + Vector3.up * 1.6f;
        private Vector3 LookDirection => Quaternion.Euler(Pitch, transform.eulerAngles.y, 0f) * Vector3.forward;

        public override void Spawned()
        {
            _all.Add(this);
            if (HasStateAuthority)
                IsGazeEnabled = _enabledOnSpawn;
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _all.Remove(this);

        public override void FixedUpdateNetwork()
        {
            // Yaw already travels with the NetworkTransform; pitch only lives on the camera, so publish it.
            if (HasStateAuthority && FirstPersonCamera.Instance != null)
                Pitch = FirstPersonCamera.Instance.Pitch;
        }

        /// <summary>Turns the gaze on or off. Anyone may call it (debug panel); the camper's owner applies it.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SetGazeEnabled(NetworkBool enabled) => IsGazeEnabled = enabled;

        /// <summary>True if <paramref name="point"/> (belonging to <paramref name="target"/>) is in the petrifying cone and not hidden.</summary>
        public bool CanSee(Vector3 point, Transform target)
        {
            if (!IsGazeEnabled)
                return false;

            var eye = EyePosition;
            var toPoint = point - eye;
            var distance = toPoint.magnitude;
            if (distance > _range || Vector3.Angle(LookDirection, toPoint) > _halfAngle)
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

        private void OnDrawGizmosSelected()
        {
            var eye = EyePosition;
            var forward = Object != null && Object.IsValid ? LookDirection : transform.forward;
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
            foreach (var axis in new[] { Vector3.up, transform.right })
            {
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(_halfAngle, axis) * forward * _range);
                Gizmos.DrawLine(eye, eye + Quaternion.AngleAxis(-_halfAngle, axis) * forward * _range);
            }
        }
    }
}
