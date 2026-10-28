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
        [Tooltip("A bucket can be filled when its holder is closer than this (m, horizontally) to the line.")]
        [SerializeField, Min(0f)] private float _fillRange = 1.5f;

        /// <summary>True when <paramref name="position"/> is within the fill range (plus <paramref name="extra"/>) of any enabled source.</summary>
        public static bool IsNearAny(Vector3 position, float extra)
        {
            foreach (var source in Sources)
            {
                if (source.HorizontalDistance(position) <= source._fillRange + extra)
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
