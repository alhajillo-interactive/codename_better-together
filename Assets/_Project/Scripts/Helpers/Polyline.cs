using UnityEngine;

namespace BetterTogether.Helpers
{
    /// <summary>
    /// Geometry helpers for world-space polylines (rivers, shores...).
    /// </summary>
    public static class Polyline
    {
        /// <summary>
        /// The point of the polyline closest to <paramref name="point"/>. With <paramref name="horizontal"/> the height is
        /// ignored when picking the closest point (the result still lies on the line, at the line's own height).
        /// An empty path returns the input point. Allocation-free.
        /// </summary>
        public static Vector3 ClosestPoint(Vector3[] path, Vector3 point, bool horizontal = false)
        {
            if (path == null || path.Length == 0)
                return point;

            var best = path[0];
            var bestSqr = float.MaxValue;
            for (var i = 0; i < path.Length - 1; i++)
            {
                var a = path[i];
                var full = path[i + 1] - a;
                var segment = full;
                var toPoint = point - a;
                if (horizontal)
                {
                    segment.y = 0f;
                    toPoint.y = 0f;
                }

                var length = segment.sqrMagnitude;
                var t = length > 0.0001f ? Mathf.Clamp01(Vector3.Dot(toPoint, segment) / length) : 0f;
                var candidate = a + full * t;
                var offset = candidate - point;
                if (horizontal)
                    offset.y = 0f;

                var sqr = offset.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
