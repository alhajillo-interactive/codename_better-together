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
        [Tooltip("Seconds of night at the end of the match, whatever its duration (the sky reaches sunset this many seconds before the end).")]
        [Min(1)][SerializeField] private float _nightSeconds = 90f;
        [Tooltip("Seconds before nightfall over which dusk builds up (0 = day, 1 = nightfall).")]
        [Min(1)][SerializeField] private float _duskSeconds = 60f;
        [Tooltip("Seconds after nightfall over which the night look fades fully in.")]
        [Min(0.1f)][SerializeField] private float _nightFadeSeconds = 20f;
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
                var match = MatchManager.Instance;
                if (match != null)
                {
                    // Derived from the replicated timer: the day runs until _nightSeconds before the end, then the night.
                    var remaining = SecondsLeft(match);
                    var daySpan = Mathf.Max(0.01f, match.MatchDuration - _nightSeconds);
                    var untilNight = remaining - _nightSeconds;
                    return untilNight > 0f
                        ? Mathf.Lerp(_startTimeOfDay, SunsetTime, 1f - untilNight / daySpan)
                        : Mathf.Lerp(SunsetTime, _endTimeOfDay, Mathf.Clamp01(-untilNight / _nightSeconds));
                }

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
                    return Mathf.Max(0f, SecondsLeft(match) - _nightSeconds);

                if (!_loopWithoutMatch)
                    return Mathf.Max(0f, (SunsetTime - _startTimeOfDay) * _loopDuration);

                return Mathf.Max(0f, (SunsetTime - TimeOfDay) * _loopDuration);
            }
        }

        /// <summary>0 in full day, rising to 1 at nightfall over the dusk window; 1 during the night.</summary>
        public float Dusk01
        {
            get
            {
                if (IsNight)
                    return 1f;

                return 1f - Mathf.Clamp01(SecondsUntilNight / _duskSeconds);
            }
        }

        /// <summary>0 until nightfall, then rises to 1 over the night fade window.</summary>
        public float Night01
        {
            get
            {
                var match = MatchManager.Instance;
                if (match != null)
                    return Mathf.Clamp01((_nightSeconds - SecondsLeft(match)) / _nightFadeSeconds);

                return IsNight ? 1f : 0f;
            }
        }

        // Match seconds left, from the same local progress the sky always used (valid for late joiners and after the end).
        private static float SecondsLeft(MatchManager match) => (1f - MatchManager.MatchProgress) * match.MatchDuration;

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
