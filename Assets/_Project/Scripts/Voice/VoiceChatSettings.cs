using BetterTogether.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Voice
{
    /// <summary>
    /// Single place to tune voice chat loudness, microphone detection, push-to-talk and how voices (and gnome chatter) carry over distance.
    /// </summary>
    [CreateAssetMenu(menuName = "Better Together/Voice Chat Settings")]
    public class VoiceChatSettings : ScriptableObject
    {
        [Tooltip("Microphone amplification applied before sending. High values can distort loud voices.")]
        [Range(1f, 10f)] [SerializeField] private float _micGain = 3f;

        [Tooltip("Voice activation level (0-1). Compared against the raw microphone signal, before Mic Gain. Background noise needs a value above it to stay silent.")]
        [SerializeField, Range(0.005f, 0.2f)] private float _voiceDetectionThreshold = 0.02f;

        [Tooltip("How long the microphone stays open after the voice drops below the threshold, in milliseconds.")]
        [SerializeField, Min(0)] private int _voiceDetectionDelayMs = 300;

        [Tooltip("If on, the microphone only transmits while the push-to-talk key is held. If off, voice activation decides.")]
        [SerializeField] private bool _pushToTalk;

        [Tooltip("Key to hold while push-to-talk is on.")]
        [SerializeField] private Key _pushToTalkKey = Key.V;

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
        public float VoiceDetectionThreshold => _voiceDetectionThreshold;
        public int VoiceDetectionDelayMs => _voiceDetectionDelayMs;
        public bool PushToTalk => _pushToTalk;
        public Key PushToTalkKey => _pushToTalkKey;

        public void ApplyToVoice(AudioSource source) => Apply(source, _voiceVolume);

        public void ApplyToChatter(AudioSource source) => Apply(source, _chatterVolume);

        private void Apply(AudioSource source, float volume)
        {
            source.volume = volume;
            // Voice group of the main mixer, so players can set voice volume apart from music and effects.
            source.outputAudioMixerGroup = AudioManager.VoiceGroup;
            source.spatialBlend = _spatialBlend;
            source.rolloffMode = _rolloff;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance;
        }
    }
}
