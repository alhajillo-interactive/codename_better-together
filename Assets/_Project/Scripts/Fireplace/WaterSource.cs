using System.Collections.Generic;
using BetterTogether.Helpers;
using UnityEngine;

namespace BetterTogether.Fireplace
{
    /// <summary>
    /// Where a bucket can be filled: a world-space polyline along the shore (copy the points of the river audio and
    /// adjust them to the real water edge). Purely local geometry, no network state.
    /// </summary>
    public class WaterSource : MonoBehaviour
    {
        private static readonly List<WaterSource> Sources = new();

        [Tooltip("World-space polyline of the shore. One point is enough for a pond.")]
        [SerializeField] private Vector3[] _path;
        [Tooltip("A bucket can be filled when its holder is closer than this (m, horizontally) to the line or to a water volume.")]
        [SerializeField, Min(0f)] private float _fillRange = 1.5f;
        [Tooltip("Water surfaces (e.g. the river trigger planes). Standing in or next to any of them fills the bucket, not only at the shore line.")]
        [SerializeField] private Collider[] _volumes;
        [Tooltip("How far above a water volume's top the holder may stand and still fill (m).")]
        [SerializeField, Min(0f)] private float _maxHeightAboveWater = 2f;

        /// <summary>True when <paramref name="position"/> is within the fill range (plus <paramref name="extra"/>) of any enabled source.</summary>
        public static bool IsNearAny(Vector3 position, float extra)
        {
            foreach (var source in Sources)
            {
                var range = source._fillRange + extra;
                if (source.InVolume(position, range) || source.HorizontalDistance(position) <= range)
                    return true;
            }

            return false;
        }

        // Static geometry, so every peer (and the authority validating the fill RPC) gets the same answer.
        private bool InVolume(Vector3 position, float range)
        {
            if (_volumes == null)
                return false;

            foreach (var volume in _volumes)
            {
                if (volume == null)
                    continue;

                var bounds = volume.bounds;
                if (position.y > bounds.max.y + _maxHeightAboveWater)
                    continue;

                if (position.x >= bounds.min.x - range && position.x <= bounds.max.x + range
                    && position.z >= bounds.min.z - range && position.z <= bounds.max.z + range)
                    return true;
            }

            return false;
        }

        private void OnEnable() => Sources.Add(this);

        private void OnDisable() => Sources.Remove(this);

        private float HorizontalDistance(Vector3 position)
        {
            if (_path == null || _path.Length == 0)
                return float.MaxValue;

            var closest = _path.Length == 1 ? _path[0] : Polyline.ClosestPoint(_path, position, true);
            var offset = closest - position;
            return new Vector2(offset.x, offset.z).magnitude;
        }

        private void OnDrawGizmosSelected()
        {
            if (_path == null)
                return;

            Gizmos.color = Color.cyan;
            for (var i = 0; i < _path.Length - 1; i++)
                Gizmos.DrawLine(_path[i], _path[i + 1]);

            foreach (var point in _path)
                Gizmos.DrawWireSphere(point, _fillRange);
        }
    }
}
