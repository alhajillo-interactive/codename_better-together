using UnityEngine;
using UnityEngine.Rendering;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Presentation of the approaching night: drives the weight of a global Volume (white balance and exposure
    /// overrides authored in its profile) and the scene fog from the <see cref="DayNightCycle"/>. Dusk fades the
    /// look in, the night completes it. It owns no state and writes no networked data: everything derives from the
    /// match timer, so late joiners and a new master see the same light.
    /// </summary>
    [RequireComponent(typeof(DayNightCycle))]
    public class DayNightFeel : MonoBehaviour
    {
        [Tooltip("Global Volume whose profile holds the night look. Its weight is driven here.")]
        [SerializeField] private Volume _volume;
        [Tooltip("Volume weight reached at nightfall from dusk alone (the night then takes it to 1).")]
        [Range(0f, 1f)][SerializeField] private float _duskVolumeWeight = 0.5f;

        [Header("Fog")]
        [SerializeField] private Color _dayFogColor = new Color(0.7f, 0.75f, 0.85f);
        [SerializeField] private Color _nightFogColor = new Color(0.04f, 0.06f, 0.12f);
        [Tooltip("Exponential-squared density at full night; 0.1 leaves about a third of the view at 10 m.")]
        [Min(0f)][SerializeField] private float _nightFogDensity = 0.1f;
        [Tooltip("Density reached at nightfall from dusk alone.")]
        [Min(0f)][SerializeField] private float _duskFogDensity = 0.01f;

        private DayNightCycle _cycle;
        private bool _savedFog;
        private bool _fogWas;
        private FogMode _fogModeWas;
        private Color _fogColorWas;
        private float _fogDensityWas;

        private void Awake() => _cycle = GetComponent<DayNightCycle>();

        private void OnEnable()
        {
            _savedFog = true;
            _fogWas = RenderSettings.fog;
            _fogModeWas = RenderSettings.fogMode;
            _fogColorWas = RenderSettings.fogColor;
            _fogDensityWas = RenderSettings.fogDensity;
        }

        private void OnDisable()
        {
            if (!_savedFog)
                return;

            RenderSettings.fog = _fogWas;
            RenderSettings.fogMode = _fogModeWas;
            RenderSettings.fogColor = _fogColorWas;
            RenderSettings.fogDensity = _fogDensityWas;
            if (_volume != null)
                _volume.weight = 0f;
        }

        private void LateUpdate()
        {
            var dusk = _cycle.Dusk01;
            var night = _cycle.Night01;

            // Dusk builds up to the dusk level at nightfall, the night fades from there to the full look.
            var weight = Mathf.Lerp(dusk * _duskVolumeWeight, 1f, night);
            var density = Mathf.Lerp(dusk * _duskFogDensity, _nightFogDensity, night);

            if (_volume != null)
                _volume.weight = weight;

            RenderSettings.fog = density > 0.0001f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = density;
            RenderSettings.fogColor = Color.Lerp(_dayFogColor, _nightFogColor, Mathf.Max(night, dusk * 0.5f));
        }
    }
}
