using UnityEngine;

namespace BetterTogether.Voice
{
    /// <summary>
    /// Single place to tune voice chat loudness: microphone gain and how voices (and gnome chatter) carry over distance.
    /// </summary>
    [CreateAssetMenu(menuName = "Better Together/Voice Chat Settings")]
    public class VoiceChatSettings : ScriptableObject
    {
        [Tooltip("Microphone amplification applied before sending. High values can distort loud voices.")]
        [Range(1f, 10f)] [SerializeField] private float _micGain = 3f;

        [Tooltip("Volume of players' voices.")]
        [Range(0f, 1f)] [SerializeField] private float _voiceVolume = 1f;

        [Tooltip("Volume of the gnome gibberish heard by campers.")]
        [Range(0f, 1f)] [SerializeField] private float _chatterVolume = 1f;

        [Tooltip("0 = same volume everywhere, 1 = full 3D positional audio.")]
        [Range(0f, 1f)] [SerializeField] private float _spatialBlend = 1f;

        [SerializeField] private AudioRolloffMode _rolloff = AudioRolloffMode.Linear;

        [Tooltip("Full volume inside this radius.")]
        [SerializeField] private float _minDistance = 4f;

        [Tooltip("Silent beyond this radius.")]
        [SerializeField] private float _maxDistance = 30f;

        public float MicGain => _micGain;

        public void ApplyToVoice(AudioSource source) => Apply(source, _voiceVolume);

        public void ApplyToChatter(AudioSource source) => Apply(source, _chatterVolume);

        private void Apply(AudioSource source, float volume)
        {
            source.volume = volume;
            source.spatialBlend = _spatialBlend;
            source.rolloffMode = _rolloff;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance;
        }
    }
}
