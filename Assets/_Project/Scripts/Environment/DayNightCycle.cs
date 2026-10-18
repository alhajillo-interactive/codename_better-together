using Fusion;
using UnityEngine;

namespace BetterTogether.Environment
{
    [RequireComponent(typeof(SkyLighting))]
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Time of day when the session starts (0 sunrise, 0.5 sunset).")]
        [Range(0f, 1f)][SerializeField] private float _startTimeOfDay = 0.02f;
        [Tooltip("Time of day when the session ends (0.5 sunset, 1 next sunrise).")]
        [Range(0f, 1f)][SerializeField] private float _endTimeOfDay = 0.6f;

        private SkyLighting _lighting;
        private bool _isSpawned;

        /// <summary>0 at sunrise, 0.5 at sunset, back to 1 (= 0) at the next sunrise.</summary>
        public float TimeOfDay => Mathf.Lerp(_startTimeOfDay, _endTimeOfDay, GameManager.MatchProgress);

        public bool IsNight => TimeOfDay >= 0.5f;

        private void Awake()
        {
            _lighting = GetComponent<SkyLighting>();
        }

        void LateUpdate()
        {
            _lighting.SetTimeOfDay(TimeOfDay);
        }
    }
}
