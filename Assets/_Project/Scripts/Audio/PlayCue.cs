using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Plays a <see cref="SoundCue"/> without code: on enable, or by calling <see cref="Play"/> from a UnityEvent,
    /// an Animation Event or Timeline. Local to this machine like every AudioManager sound.
    /// </summary>
    public class PlayCue : MonoBehaviour
    {
        [SerializeField] private SoundCue _cue;
        [SerializeField] private bool _playOnEnable;
        [Tooltip("Play at this object's position (3D). Off = 2D.")]
        [SerializeField] private bool _atThisPosition = true;

        private void OnEnable()
        {
            if (_playOnEnable)
                Play();
        }

        public void Play()
        {
            if (_atThisPosition)
                _cue.PlayAt(transform.position);
            else
                _cue.Play();
        }
    }
}
