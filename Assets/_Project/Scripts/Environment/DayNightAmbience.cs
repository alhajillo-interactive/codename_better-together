using BetterTogether.Audio;
using UnityEngine;

namespace BetterTogether.Environment
{
    /// <summary>
    /// Loops a background ambience that follows the <see cref="DayNightCycle"/>: the day bed until sunset, then the
    /// night bed, crossfading between them. Local to each player like all music and ambience; every client derives
    /// day or night from the same match timer, so they switch together.
    /// </summary>
    [RequireComponent(typeof(DayNightCycle))]
    public class DayNightAmbience : MonoBehaviour
    {
        [SerializeField] private MusicTrack _day;
        [SerializeField] private MusicTrack _night;
        [Tooltip("Seconds of fade when the level starts.")]
        [SerializeField, Min(0f)] private float _startFade = 2f;
        [Tooltip("Seconds of crossfade between day and night at sunset.")]
        [SerializeField, Min(0f)] private float _sunsetFade = 6f;

        private DayNightCycle _cycle;
        private MusicTrack _shown;

        private void Awake() => _cycle = GetComponent<DayNightCycle>();

        private void Update()
        {
            var track = _cycle.IsNight ? _night : _day;
            if (track == _shown)
                return;

            AudioManager.PlayAmbience(track, _shown == null ? _startFade : _sunsetFade);
            _shown = track;
        }

        private void OnDisable()
        {
            AudioManager.StopAmbience(_startFade);
            _shown = null;
        }
    }
}
