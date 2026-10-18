using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Plays a <see cref="MusicTrack"/> without code: on start, or by calling <see cref="Play"/> / <see cref="Stop"/>
    /// from a UnityEvent or Timeline (e.g. a special zone or moment).
    /// </summary>
    public class PlayMusic : MonoBehaviour
    {
        [SerializeField] private MusicTrack _track;
        [SerializeField] private bool _playOnStart;
        [Tooltip("Stop this track when the object is destroyed (e.g. leaving the scene).")]
        [SerializeField] private bool _stopOnDestroy;

        private void Start()
        {
            if (_playOnStart)
                Play();
        }

        private void OnDestroy()
        {
            if (_stopOnDestroy)
                Stop();
        }

        public void Play() => AudioManager.PlayMusic(_track);

        public void Stop()
        {
            if (AudioManager.CurrentTrack == _track)
                AudioManager.StopMusic();
        }
    }
}
