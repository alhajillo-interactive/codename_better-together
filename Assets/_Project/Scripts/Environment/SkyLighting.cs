using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Presentation of the time of day: rotates the sun, fades its light and the ambient colour, and feeds the
    /// sun and moon directions to the skybox shader. TimeOfDay is 0 at sunrise, 0.25 noon, 0.5 sunset,
    /// 0.75 midnight and 1 the next sunrise. In edit mode it previews <c>_previewTimeOfDay</c>.
    /// </summary>
    [ExecuteAlways]
    public class SkyLighting : MonoBehaviour
    {
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");

        [SerializeField] private Light _sun;

        [Tooltip("Used in edit mode and before the network clock runs.")]
        [Range(0f, 1f)] [SerializeField] private float _previewTimeOfDay = 0.3f;

        [SerializeField] private float _sunYaw = 170f;
        [SerializeField] private float _sunIntensity = 1.2f;
        [SerializeField] private Color _dayAmbient = new Color(0.55f, 0.6f, 0.7f);
        [SerializeField] private Color _nightAmbient = new Color(0.06f, 0.08f, 0.15f);

        [Tooltip("The sun light fades to zero over this many degrees around the horizon.")]
        [SerializeField] private float _horizonFadeDegrees = 12f;

        private float _drivenTimeOfDay;
        private bool _isDriven;

        /// <summary>Current time of day in 0..1: the driven value when a clock sets it, otherwise the preview.</summary>
        public float TimeOfDay => _isDriven ? _drivenTimeOfDay : _previewTimeOfDay;

        /// <summary>Called by the clock every frame; takes over from the edit-mode preview.</summary>
        public void SetTimeOfDay(float t)
        {
            _drivenTimeOfDay = t;
            _isDriven = true;
        }

        private void OnEnable()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        private void OnDisable()
        {
            _isDriven = false;
        }

        private void LateUpdate()
        {
            Apply(TimeOfDay);
        }

        private void Apply(float timeOfDay)
        {
            if (_sun == null)
                return;

            _sun.transform.rotation = Quaternion.Euler(timeOfDay * 360f, _sunYaw, 0f);

            Vector3 forward = _sun.transform.forward;
            float elevation = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float light = Mathf.InverseLerp(-_horizonFadeDegrees * 0.5f, _horizonFadeDegrees, elevation);

            _sun.intensity = _sunIntensity * light;
            RenderSettings.ambientLight = Color.Lerp(_nightAmbient, _dayAmbient, light);

            Shader.SetGlobalVector(SunDirId, -forward);
            Shader.SetGlobalVector(MoonDirId, forward);
        }
    }
}
