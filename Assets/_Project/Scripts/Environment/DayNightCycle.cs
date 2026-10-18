using Fusion;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Shared day clock. The state authority (master client) advances the elapsed time and every client reads
    /// the replicated value, so all players see the same time of day; if the master leaves, the new one carries on.
    /// Day and night run at different speeds and loop forever.
    /// Night is the camper's deadline.
    /// </summary>
    [RequireComponent(typeof(SkyLighting))]
    public class DayNightCycle : NetworkBehaviour
    {
        [Tooltip("Seconds from sunrise to sunset.")]
        [Min(1)] [SerializeField] private float _dayDuration = 120f;

        [Tooltip("Seconds from sunset to the next sunrise.")]
        [Min(1)] [SerializeField] private float _nightDuration = 60f;

        [Tooltip("Time of day when the session starts (0 sunrise, 0.5 sunset).")]
        [Range(0f, 1f)] [SerializeField] private float _startTimeOfDay = 0.02f;

        private SkyLighting _lighting;
        private bool _isSpawned;

        [Networked] private float Elapsed { get; set; }
        [Networked] private NetworkBool Started { get; set; }

        /// <summary>0 at sunrise, 0.5 at sunset, back to 1 (= 0) at the next sunrise.</summary>
        public float TimeOfDay => _isSpawned ? ToTimeOfDay(Elapsed) : _startTimeOfDay;

        public bool IsNight => TimeOfDay >= 0.5f;

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
            if (HasStateAuthority)
                Elapsed += Runner.DeltaTime;
        }

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
