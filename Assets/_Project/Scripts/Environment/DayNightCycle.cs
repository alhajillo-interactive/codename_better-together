using BetterTogether.Match;
using Fusion;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Shared day clock. The state authority (master client) advances the elapsed time and every client reads
    /// the replicated value, so all players see the same time of day; if the master leaves, the new one carries on.
    /// Day and night run at different speeds. Without a <see cref="MatchManager"/> they loop forever; with one,
    /// the clock only runs while a match is playing and stops shortly after nightfall.
    /// Night is the camper's deadline.
    /// </summary>
    [RequireComponent(typeof(SkyLighting))]
    public class DayNightCycle : NetworkBehaviour, IMatchResettable
    {
        [Tooltip("Seconds from sunrise to sunset.")]
        [Min(1)] [SerializeField] private float _dayDuration = 120f;

        [Tooltip("Seconds from sunset to the next sunrise.")]
        [Min(1)] [SerializeField] private float _nightDuration = 60f;

        [Tooltip("Time of day when the session starts (0 sunrise, 0.5 sunset).")]
        [Range(0f, 1f)] [SerializeField] private float _startTimeOfDay = 0.02f;

        [Tooltip("Without a match manager, keep looping day and night forever. Otherwise the clock stops at night.")]
        [SerializeField] private bool _loopWithoutMatch = true;

        [Tooltip("Seconds of night the clock runs after sunset during a match, so the sky gets dark.")]
        [Min(0)] [SerializeField] private float _nightHoldSeconds = 8f;

        private SkyLighting _lighting;
        private bool _isSpawned;

        [Networked] private float Elapsed { get; set; }
        [Networked] private NetworkBool Started { get; set; }

        /// <summary>0 at sunrise, 0.5 at sunset, back to 1 (= 0) at the next sunrise.</summary>
        public float TimeOfDay => _isSpawned ? ToTimeOfDay(Elapsed) : _startTimeOfDay;

        public bool IsNight => TimeOfDay >= 0.5f;

        /// <summary>Seconds of daylight left; 0 at night or before the clock exists.</summary>
        public float SecondsUntilNight => _isSpawned && !IsNight ? Mathf.Max(0f, _dayDuration - CycleSeconds) : 0f;

        /// <summary>0 at the start of the day, 1 at nightfall (and during the night).</summary>
        public float DayProgress01 => _isSpawned ? Mathf.Clamp01(CycleSeconds / _dayDuration) : 0f;

        private float CycleSeconds => Mathf.Repeat(Elapsed, _dayDuration + _nightDuration);

        /// <summary>1 for the first day, then one more per completed day/night cycle.</summary>
        public int DayNumber => _isSpawned ? Mathf.FloorToInt(Elapsed / (_dayDuration + _nightDuration)) + 1 : 1;

        private void Awake()
        {
            _lighting = GetComponent<SkyLighting>();
        }

        public override void Spawned()
        {
            _isSpawned = true;

            if (HasStateAuthority && !Started)
            {
                Elapsed = ToSeconds(_startTimeOfDay);
                Started = true;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var match = MatchManager.Instance;
            if (match == null)
            {
                if (_loopWithoutMatch)
                    Elapsed += Runner.DeltaTime;
                else
                    Elapsed = Mathf.Min(Elapsed + Runner.DeltaTime, _dayDuration + _nightHoldSeconds);
                return;
            }

            if (match.Phase == MatchPhase.Playing)
                Elapsed = Mathf.Min(Elapsed + Runner.DeltaTime, _dayDuration + _nightHoldSeconds);
        }

        /// <summary>Authority only: the day starts over, at the configured start time.</summary>
        public void StartMatchDay()
        {
            Elapsed = ToSeconds(_startTimeOfDay);
            Started = true;
        }

        /// <summary>Authority only: back to the start of the day.</summary>
        public void ResetForMatch() => StartMatchDay();

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        public override void Render()
        {
            _lighting.SetTimeOfDay(TimeOfDay);
        }

        private float ToTimeOfDay(float seconds)
        {
            float t = Mathf.Repeat(seconds, _dayDuration + _nightDuration);
            return t < _dayDuration
                ? 0.5f * t / _dayDuration
                : 0.5f + 0.5f * (t - _dayDuration) / _nightDuration;
        }

        private float ToSeconds(float timeOfDay)
        {
            return timeOfDay < 0.5f
                ? timeOfDay * 2f * _dayDuration
                : _dayDuration + (timeOfDay - 0.5f) * 2f * _nightDuration;
        }
    }
}
