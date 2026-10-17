using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Shared day clock. The master client starts it and everyone derives the sun position from the same
    /// network timer, so all players see the same time of day. Night is the camper's deadline.
    /// </summary>
    public class DayNightCycle : NetworkBehaviour
    {
        [SerializeField] private Light _sun;

        [Tooltip("Seconds from sunrise to nightfall.")]
        [Min(1)] [SerializeField] private float _dayDuration = 300f;

        [Tooltip("Sun pitch at sunrise (degrees above the horizon, morning).")]
        [SerializeField] private float _startAngle = 10f;

        [Tooltip("Sun pitch when night falls (past 180 the sun is below the horizon).")]
        [SerializeField] private float _nightAngle = 200f;

        [SerializeField] private float _sunYaw = 170f;
        [SerializeField] private float _sunIntensity = 1.2f;
        [SerializeField] private Color _dayAmbient = new Color(0.55f, 0.6f, 0.7f);
        [SerializeField] private Color _nightAmbient = new Color(0.06f, 0.08f, 0.15f);

        [Tooltip("The sun light fades to zero over this many degrees around the horizon.")]
        [SerializeField] private float _horizonFadeDegrees = 12f;

        private bool _isSpawned;

        [Networked] private TickTimer DayTimer { get; set; }

        /// <summary>0 at sunrise, 1 when night falls.</summary>
        public float Progress01
        {
            get
            {
                if (!_isSpawned)
                    return 0f;
                if (DayTimer.Expired(Runner))
                    return 1f;
                if (!DayTimer.IsRunning)
                    return 0f;

                float remaining = DayTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(1f - remaining / _dayDuration);
            }
        }

        public bool IsNight => Progress01 >= 1f;

        public override void Spawned()
        {
            _isSpawned = true;
            RenderSettings.ambientMode = AmbientMode.Flat;

            // Temporary: a future match manager will call StartDay() instead.
            StartDay();
        }

        public override void Despawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        /// <summary>Starts the day from sunrise. Only the state authority can start it.</summary>
        public void StartDay()
        {
            if (!HasStateAuthority || DayTimer.IsRunning || DayTimer.Expired(Runner))
                return;

            DayTimer = TickTimer.CreateFromSeconds(Runner, _dayDuration);
        }

        public override void Render()
        {
            if (_sun == null)
                return;

            float pitch = Mathf.Lerp(_startAngle, _nightAngle, Progress01);
            _sun.transform.rotation = Quaternion.Euler(pitch, _sunYaw, 0f);

            float elevation = Mathf.Asin(Mathf.Clamp(-_sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float light = Mathf.InverseLerp(-_horizonFadeDegrees * 0.5f, _horizonFadeDegrees, elevation);

            _sun.intensity = _sunIntensity * light;
            RenderSettings.ambientLight = Color.Lerp(_nightAmbient, _dayAmbient, light);
        }
    }
}
