using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Where the tall grass grows: a grid of 1 m cells that matches the grass painted on the terrain, so what looks like cover is
    /// cover. Scene data without networking, the same on every peer. Built by AbrazoLevelBuilder.
    /// </summary>
    public class TallGrassField : MonoBehaviour
    {
        [SerializeField] private Vector2 _origin;
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private bool[] _cells;

        private static TallGrassField _current;

        private void Awake() => _current = this;

        private void OnDestroy()
        {
            if (_current == this)
                _current = null;
        }

        /// <summary>True when the position stands in tall grass. False in a level without any.</summary>
        public static bool Contains(Vector3 position) => _current != null && _current.IsGrass(position.x, position.z);

        private bool IsGrass(float x, float z)
        {
            var cellX = Mathf.FloorToInt(x - _origin.x);
            var cellZ = Mathf.FloorToInt(z - _origin.y);
            return cellX >= 0 && cellZ >= 0 && cellX < _width && cellZ < _height && _cells[cellZ * _width + cellX];
        }

#if UNITY_EDITOR
        /// <summary>Editor only: stores the grid the level builder computed.</summary>
        public void Configure(Vector2 origin, int width, int height, bool[] cells)
        {
            _origin = origin;
            _width = width;
            _height = height;
            _cells = cells;
        }
#endif
    }
}
