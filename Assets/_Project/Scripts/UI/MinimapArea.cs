using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Scene-side description of the area the minimap covers (C9.5): the baked top-down texture, the world rectangle it shows
    /// (XZ plane) and the fixed points of interest. No network: it is plain scene data. A scene without one has no minimap.
    /// </summary>
    public class MinimapArea : MonoBehaviour
    {
        /// <summary>A fixed point of interest drawn with the game icon of <see cref="Icon"/>.</summary>
        [Serializable]
        public struct FixedPoint
        {
            [Tooltip("World position on the XZ plane (x, z).")]
            public Vector2 Position;
            [Tooltip("Icon drawn at the point (Fire, Cage or Bucket).")]
            public UiIcon Icon;
        }

        private static readonly List<MinimapArea> Registered = new();

        [Tooltip("Baked top-down picture of the level.")]
        [SerializeField] private Texture2D _map;
        [Tooltip("World position (x, z) of the centre of the picture.")]
        [SerializeField] private Vector2 _worldCenter;
        [Tooltip("World size (x, z) in metres covered by the picture.")]
        [SerializeField] private Vector2 _worldSize = new(84f, 84f);
        [Tooltip("Fixed points of interest (fire, cage, pool).")]
        [SerializeField] private FixedPoint[] _points;

        /// <summary>The area of the loaded scene, or null when the scene has none.</summary>
        public static MinimapArea Current => Registered.Count > 0 ? Registered[Registered.Count - 1] : null;

        public Texture2D Map => _map;
        public Vector2 WorldCenter => _worldCenter;
        public Vector2 WorldSize => _worldSize;
        public IReadOnlyList<FixedPoint> Points => _points;

        private void OnEnable()
        {
            if (!Registered.Contains(this))
                Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);
    }
}
