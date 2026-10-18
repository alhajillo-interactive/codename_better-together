using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace BetterTogether.Audio
{
    /// <summary>The volume channels of the main mixer, each with its own player setting.</summary>
    public enum AudioChannel
    {
        Master,
        Music,
        Sfx,
        Voice,
    }

    /// <summary>
    /// Plays music and sound effects on this machine only (each client hears its own copy) and owns the player's
    /// volume settings. Everything goes through <c>Resources/MainMixer</c>: Master → Music, SFX and Voice (voice chat
    /// and gnome chatter route there through <c>VoiceChatSettings</c>). Created automatically at startup.
    /// <para>SFX: <c>cue.Play()</c> / <c>cue.PlayAt(position)</c> on a <see cref="SoundCue"/>.</para>
    /// <para>Music: <see cref="PlayMusic"/> / <see cref="StopMusic"/>; tracks crossfade.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AudioManager : MonoBehaviour
    {
        private const string MixerResource = "MainMixer";
        private static readonly string[] VolumeParameters = { "master_vol", "music_vol", "sfx_vol", "voice_vol" };
        // Music starts at half so it doesn't cover voice chat.
        private static readonly float[] DefaultVolumes = { 1f, 0.5f, 0.8f, 1f };

        [Tooltip("Seconds to fade between music tracks.")]
        [SerializeField, Min(0f)] private float _musicFade = 1.5f;
        [Tooltip("Default seconds to fade between ambience beds (e.g. day to night).")]
        [SerializeField, Min(0f)] private float _ambienceFade = 3f;

        private static AudioManager _instance;
        private static bool _quitting;
        private static AudioMixer _mixer;
        private static bool _mixerLoaded;

        private readonly List<AudioSource> _sfxSources = new();
        private AudioMixerGroup _sfxGroup;
        private TrackPlayer _music;
        private TrackPlayer _ambience;

        public static MusicTrack CurrentTrack => _instance != null ? _instance._music.Current : null;
        public static MusicTrack CurrentAmbience => _instance != null ? _instance._ambience.Current : null;

        public static AudioMixer Mixer
        {
            get
            {
                if (!_mixerLoaded)
                {
                    _mixerLoaded = true;
                    _mixer = Resources.Load<AudioMixer>(MixerResource);
                    if (_mixer == null)
                        Debug.LogWarning($"[AudioManager] No mixer at Resources/{MixerResource}; volume settings won't apply.");
                }

                return _mixer;
            }
        }

        public static AudioMixerGroup MusicGroup => FindGroup("Music");
        public static AudioMixerGroup SfxGroup => FindGroup("SFX");
        public static AudioMixerGroup VoiceGroup => FindGroup("Voice");

        private static AudioManager Instance
        {
            get
            {
                if (_instance == null && !_quitting)
                    new GameObject(nameof(AudioManager)).AddComponent<AudioManager>();
                return _instance;
            }
        }

        /// <summary>Player setting, 0..1. Saved between sessions.</summary>
        public static float GetVolume(AudioChannel channel) =>
            PlayerPrefs.GetFloat(VolumeKey(channel), DefaultVolumes[(int)channel]);

        public static void SetVolume(AudioChannel channel, float value)
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey(channel), value);
            ApplyVolume(channel, value);
        }

        public static void PlaySfx(SoundCue cue, Vector3? position)
        {
            if (cue == null || !cue.IsPlayable)
                return;

            var now = Time.unscaledTime;
            if (now - cue.LastPlayTime < cue.MinInterval)
                return;
            cue.LastPlayTime = now;

            Instance?.PlaySfxInternal(cue, position);
        }

        /// <summary>Crossfades to <paramref name="track"/>. Does nothing if it is already playing; null stops the music.</summary>
        public static void PlayMusic(MusicTrack track)
        {
            if (track == null)
                StopMusic();
            else
                Instance?._music.Play(track, Instance._musicFade);
        }

        public static void StopMusic()
        {
            if (_instance != null)
                _instance._music.Play(null, _instance._musicFade);
        }

        /// <summary>
        /// Crossfades the background ambience (independent from music) to <paramref name="track"/>; null stops it.
        /// Ambience goes through the SFX group. <paramref name="fadeSeconds"/> overrides the default fade.
        /// </summary>
        public static void PlayAmbience(MusicTrack track, float? fadeSeconds = null)
        {
            if (track == null)
                StopAmbience(fadeSeconds);
            else
                Instance?._ambience.Play(track, fadeSeconds ?? Instance._ambienceFade);
        }

        public static void StopAmbience(float? fadeSeconds = null)
        {
            if (_instance != null)
                _instance._ambience.Play(null, fadeSeconds ?? _instance._ambienceFade);
        }

        // Exists from the start so saved volumes (including voice) apply before anything plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateAtStartup() => _ = Instance;

        // Statics survive between Play sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _quitting = false;
            _mixerLoaded = false;
            _mixer = null;
        }

        private static string VolumeKey(AudioChannel channel) => $"audio.{channel.ToString().ToLowerInvariant()}";

        private static void ApplyVolume(AudioChannel channel, float value)
        {
            // 0..1 slider to decibels; the mixer floor is -80 dB.
            var mixer = Mixer;
            if (mixer != null)
                mixer.SetFloat(VolumeParameters[(int)channel], value > 0.0001f ? Mathf.Log10(value) * 20f : -80f);
        }

        private static AudioMixerGroup FindGroup(string groupName)
        {
            var mixer = Mixer;
            if (mixer == null)
                return null;

            var groups = mixer.FindMatchingGroups(groupName);
            return groups.Length > 0 ? groups[0] : null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _sfxGroup = SfxGroup;
            var musicGroup = MusicGroup;
            _music = new TrackPlayer(CreateSource("Music", musicGroup), CreateSource("Music", musicGroup));
            _ambience = new TrackPlayer(CreateSource("Ambience", _sfxGroup), CreateSource("Ambience", _sfxGroup));
        }

        // Mixer values set during Awake can be ignored by Unity, so apply them here.
        private void Start()
        {
            foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
                ApplyVolume(channel, GetVolume(channel));
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit() => _quitting = true;

        private void Update()
        {
            _music.Tick(Time.unscaledDeltaTime);
            _ambience.Tick(Time.unscaledDeltaTime);
        }

        private void PlaySfxInternal(SoundCue cue, Vector3? position)
        {
            var source = GetFreeSfxSource();
            var spatial = cue.Spatial && position.HasValue;

            source.transform.position = spatial ? position.Value : transform.position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.maxDistance = Mathf.Max(1f, cue.MaxDistance);
            source.resource = cue.PickResource();
            source.volume = cue.Volume;
            source.pitch = Random.Range(cue.PitchRange.x, cue.PitchRange.y);
            source.Play();
        }

        private AudioSource GetFreeSfxSource()
        {
            foreach (var source in _sfxSources)
            {
                if (!source.isPlaying)
                    return source;
            }

            var created = CreateSource("SFX", _sfxGroup);
            created.rolloffMode = AudioRolloffMode.Linear;
            created.minDistance = 1f;
            _sfxSources.Add(created);
            return created;
        }

        private AudioSource CreateSource(string sourceName, AudioMixerGroup group)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = group;
            return source;
        }

        /// <summary>Plays one looping track at a time and crossfades between two sources when it changes.</summary>
        private sealed class TrackPlayer
        {
            private readonly Channel[] _channels;
            private int _active;
            private float _fade;

            public TrackPlayer(AudioSource a, AudioSource b) => _channels = new[] { new Channel(a), new Channel(b) };

            public MusicTrack Current { get; private set; }

            public void Play(MusicTrack track, float fadeSeconds)
            {
                // A non-looping track that already ended can be played again.
                if (track == Current && (track == null || _channels[_active].IsPlaying))
                    return;

                Current = track;
                _fade = fadeSeconds;
                _channels[_active].FadeOut();

                if (track == null || track.Clip == null)
                    return;

                _active = 1 - _active;
                _channels[_active].FadeIn(track);
            }

            public void Tick(float deltaTime)
            {
                var step = _fade > 0f ? deltaTime / _fade : 1f;
                foreach (var channel in _channels)
                    channel.Tick(step);
            }
        }

        /// <summary>One of the two sources of a <see cref="TrackPlayer"/>.</summary>
        private sealed class Channel
        {
            private readonly AudioSource _source;
            private float _level;
            private float _target;
            private float _trackVolume;

            public Channel(AudioSource source)
            {
                _source = source;
                _source.spatialBlend = 0f;
            }

            public bool IsPlaying => _source.isPlaying;

            public void FadeIn(MusicTrack track)
            {
                _source.clip = track.Clip;
                _source.loop = track.Loop;
                _trackVolume = track.Volume;
                _level = 0f;
                _target = 1f;
                _source.volume = 0f;
                _source.Play();
            }

            public void FadeOut() => _target = 0f;

            public void Tick(float step)
            {
                if (!_source.isPlaying)
                    return;

                _level = Mathf.MoveTowards(_level, _target, step);
                _source.volume = _level * _trackVolume;

                if (_level <= 0f && _target <= 0f)
                    _source.Stop();
            }
        }
    }
}
