using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// The playable area of the level, in world space. Loose items that stay outside it for too long are returned
    /// to their start pose by the <c>Carryable</c> component. Not networked: every peer has the same box in the scene.
    /// </summary>
    public class LevelBounds : MonoBehaviour
    {
        [Tooltip("World-space box of the playable level: center and size. Items resting outside it for too long are returned. " +
                 "Adjust it to the camp: the default is centered at the world origin and is only a placeholder.")]
        [SerializeField] private Bounds _bounds = new(Vector3.zero, new Vector3(40f, 20f, 40f));

        /// <summary>The level bounds of the loaded scene, or null when the scene has none.</summary>
        public static LevelBounds Instance { get; private set; }

        /// <summary>Whether a world-space point lies inside the level box.</summary>
        public bool Contains(Vector3 point) => _bounds.Contains(point);

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"{name}: another LevelBounds ({Instance.name}) is already active; this one replaces it.", this);

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(_bounds.center, _bounds.size);
        }
    }
}
