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

        public const string PushToTalkPrefsKey = "gm_ptt";

        // -2 = not read yet, -1 = no saved choice, 0/1 = saved choice. Static: one local player per machine.
        private static int _pushToTalkOverride = -2;

        private static int Override
        {
            get
            {
                if (_pushToTalkOverride == -2)
                    _pushToTalkOverride = PlayerPrefs.HasKey(PushToTalkPrefsKey) ? PlayerPrefs.GetInt(PushToTalkPrefsKey) : -1;
                return _pushToTalkOverride;
            }
        }

        // Statics survive between Play sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _pushToTalkOverride = -2;

        /// <summary>Saves the player's microphone mode (true = push to talk) and applies it at once.</summary>
        public void SetPushToTalk(bool value)
        {
            _pushToTalkOverride = value ? 1 : 0;
            PlayerPrefs.SetInt(PushToTalkPrefsKey, _pushToTalkOverride);
        }

        public float MicGain => _micGain;
        public float VoiceDetectionThreshold => _voiceDetectionThreshold;
        public int VoiceDetectionDelayMs => _voiceDetectionDelayMs;
        /// <summary>Player setting «gm_ptt» (set by the settings panel) wins over the asset default. Cached: read every frame.</summary>
        public bool PushToTalk => (Override >= 0 ? Override == 1 : _pushToTalk);
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
