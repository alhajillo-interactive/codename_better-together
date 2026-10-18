using BetterTogether.Match;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Presentation of the day: the sky follows the match timer, from <c>_startTimeOfDay</c> when the match begins to
    /// <c>_endTimeOfDay</c> when it runs out. It owns no state; without a <see cref="MatchManager"/> in the scene the
    /// sky holds at <c>_startTimeOfDay</c> (or loops through the day slowly when <c>_loopWithoutMatch</c> is on).
    /// </summary>
    [RequireComponent(typeof(SkyLighting))]
    public class DayNightCycle : MonoBehaviour
    {
        private const float SunsetTime = 0.5f;

        [Tooltip("Time of day when the match starts (0 sunrise, 0.5 sunset).")]
        [Range(0f, 1f)][SerializeField] private float _startTimeOfDay = 0.02f;
        [Tooltip("Time of day when the match ends (0.5 sunset, 1 next sunrise).")]
        [Range(0f, 1f)][SerializeField] private float _endTimeOfDay = 0.6f;
        [Tooltip("Seconds a full day takes when there is no match manager in the scene.")]
        [Min(1)][SerializeField] private float _loopDuration = 240f;
        [Tooltip("Testing scenes without a match manager: loop through the day instead of holding at the start.")]
        [SerializeField] private bool _loopWithoutMatch;

        private SkyLighting _lighting;

        /// <summary>0 at sunrise, 0.5 at sunset, back to 1 (= 0) at the next sunrise.</summary>
        public float TimeOfDay
        {
            get
            {
                if (MatchManager.Instance != null)
                    return Mathf.Lerp(_startTimeOfDay, _endTimeOfDay, MatchManager.MatchProgress);

                if (!_loopWithoutMatch)
                    return _startTimeOfDay;

                return Mathf.Repeat(_startTimeOfDay + Time.time / _loopDuration, 1f);
            }
        }

        public bool IsNight => TimeOfDay >= SunsetTime;

        /// <summary>0 at the start of the day, 1 at nightfall (and during the night).</summary>
        public float DayProgress01 => Mathf.Clamp01(Mathf.InverseLerp(_startTimeOfDay, SunsetTime, TimeOfDay));

        /// <summary>Seconds of daylight left; 0 at night.</summary>
        public float SecondsUntilNight
        {
            get
            {
                var match = MatchManager.Instance;
                if (match != null)
                    return Mathf.Max(0f, (SunsetProgress - MatchManager.MatchProgress) * match.MatchDuration);

                if (!_loopWithoutMatch)
                    return Mathf.Max(0f, (SunsetTime - _startTimeOfDay) * _loopDuration);

                return Mathf.Max(0f, (SunsetTime - TimeOfDay) * _loopDuration);
            }
        }

        /// <summary>Fraction of the match at which the sky reaches sunset.</summary>
        private float SunsetProgress => Mathf.Clamp01(Mathf.InverseLerp(_startTimeOfDay, _endTimeOfDay, SunsetTime));

        private void Awake()
        {
            _lighting = GetComponent<SkyLighting>();
        }

        private void LateUpdate()
        {
            _lighting.SetTimeOfDay(TimeOfDay);
        }
    }
}
