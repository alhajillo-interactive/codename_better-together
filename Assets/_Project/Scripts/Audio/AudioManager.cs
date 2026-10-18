using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Plays music and sound effects on this machine only (each client hears its own copy; voice chat is separate).
    /// Creates itself on first use and survives scene loads, so no setup is needed. A scene or prefab instance
    /// can be added instead to set the optional mixer groups.
    /// <para>SFX: <c>cue.Play()</c> / <c>cue.PlayAt(position)</c> on a <see cref="SoundCue"/>.</para>
    /// <para>Music: <see cref="PlayMusic"/> / <see cref="StopMusic"/>; tracks crossfade.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AudioManager : MonoBehaviour
    {
        private const string MusicVolumeKey = "audio.music";
        private const string SfxVolumeKey = "audio.sfx";

        [Header("Optional mixer routing")]
        [SerializeField] private AudioMixerGroup _musicGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;

        [Header("Music")]
        [Tooltip("Seconds to fade between tracks.")]
        [SerializeField, Min(0f)] private float _musicFade = 1.5f;

        private static AudioManager _instance;
        private static bool _quitting;

        private readonly List<AudioSource> _sfxSources = new();
        private readonly MusicChannel[] _music = new MusicChannel[2];
        private int _activeMusic;
        private MusicTrack _currentTrack;

        /// <summary>Player setting, 0..1. Saved between sessions. Default leaves room for voice chat.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicVolumeKey, 0.5f);
            set => PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
        }

        /// <summary>Player setting, 0..1. Saved between sessions.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f);
            set => PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
        }

        public static MusicTrack CurrentTrack => _instance != null ? _instance._currentTrack : null;

        private static AudioManager Instance
        {
            get
            {
                if (_instance == null && !_quitting)
                    new GameObject(nameof(AudioManager)).AddComponent<AudioManager>();
                return _instance;
            }
        }

        public static void PlaySfx(SoundCue cue, Vector3? position)
        {
            if (cue == null || cue.Sound == null)
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
                Instance?.SwitchMusic(track);
        }

        public static void StopMusic()
        {
            if (_instance != null)
                _instance.SwitchMusic(null);
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

            for (var i = 0; i < _music.Length; i++)
                _music[i] = new MusicChannel(CreateSource("Music", _musicGroup));
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit() => _quitting = true;

        // Statics survive between Play sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _quitting = false;

        private void Update()
        {
            var step = _musicFade > 0f ? Time.unscaledDeltaTime / _musicFade : 1f;
            var musicVolume = MusicVolume;
            foreach (var channel in _music)
                channel.Tick(step, musicVolume);
        }

        private void PlaySfxInternal(SoundCue cue, Vector3? position)
        {
            var source = GetFreeSfxSource();
            var spatial = cue.Spatial && position.HasValue;

            source.transform.position = spatial ? position.Value : transform.position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.maxDistance = Mathf.Max(1f, cue.MaxDistance);
            source.resource = cue.Sound;
            source.volume = cue.Volume * SfxVolume;
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

        private void SwitchMusic(MusicTrack track)
        {
            // A non-looping track that already ended can be played again.
            if (track == _currentTrack && (track == null || _music[_activeMusic].IsPlaying))
                return;

            _currentTrack = track;
            _music[_activeMusic].FadeOut();

            if (track == null || track.Clip == null)
                return;

            _activeMusic = 1 - _activeMusic;
            _music[_activeMusic].FadeIn(track);
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

        /// <summary>One of the two music sources used for crossfading.</summary>
        private sealed class MusicChannel
        {
            private readonly AudioSource _source;
            private float _level;
            private float _target;
            private float _trackVolume;

            public MusicChannel(AudioSource source)
            {
                _source = source;
                _source.spatialBlend = 0f;
            }

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

            public bool IsPlaying => _source.isPlaying;

            public void FadeOut() => _target = 0f;

            public void Tick(float step, float musicVolume)
            {
                if (!_source.isPlaying)
                    return;

                _level = Mathf.MoveTowards(_level, _target, step);
                _source.volume = _level * _trackVolume * musicVolume;

                if (_level <= 0f && _target <= 0f)
                    _source.Stop();
            }
        }
    }
}
