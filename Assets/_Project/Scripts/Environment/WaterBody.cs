using System.Collections.Generic;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// A round pool of water, defined by its centre, radius and surface height (world space). Scene-only data, no network
    /// state: every peer has the same scene, so players and items query it locally. A scene without a WaterBody behaves
    /// as if there were no water at all. The visible disc may bob a few centimetres, but this surface height never moves.
    /// </summary>
    public class WaterBody : MonoBehaviour
    {
        [Tooltip("World X/Z of the pool centre. Set by the level builder.")]
        [SerializeField] private Vector2 _centerXZ;
        [Tooltip("Radius of the pool (m).")]
        [SerializeField, Min(0.1f)] private float _radius = 10f;
        [Tooltip("World Y of the water surface (m). Physics uses this value, not the bobbing of the visual disc.")]
        [SerializeField] private float _topY;

        private static readonly List<WaterBody> Bodies = new();

        public Vector2 CenterXZ => _centerXZ;
        public float Radius => _radius;
        public float TopY => _topY;

        /// <summary>Configures the pool (used by the level builder).</summary>
        public void Configure(Vector2 centerXZ, float radius, float topY)
        {
            _centerXZ = centerXZ;
            _radius = radius;
            _topY = topY;
        }

        private void OnEnable() => Bodies.Add(this);

        private void OnDisable() => Bodies.Remove(this);

        /// <summary>True when the horizontal position is inside a pool; <paramref name="topY"/> is then its surface height. Allocation-free.</summary>
        public static bool TryGetSurface(Vector3 position, out float topY)
        {
            for (var i = 0; i < Bodies.Count; i++)
            {
                var body = Bodies[i];
                var dx = position.x - body._centerXZ.x;
                var dz = position.z - body._centerXZ.y;
                if (dx * dx + dz * dz <= body._radius * body._radius)
                {
                    topY = body._topY;
                    return true;
                }
            }

            topY = 0f;
            return false;
        }
    }
}
