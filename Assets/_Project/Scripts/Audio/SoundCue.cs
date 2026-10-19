using UnityEngine;
using UnityEngine.Audio;

namespace BetterTogether.Audio
{
    /// <summary>
    /// One sound effect and how it should play. Create with Create > Better Together > Audio > Sound Cue,
    /// drop a clip (or an Audio Random Container for variations) in <see cref="Sound"/>, and reference the asset
    /// from gameplay code. A cue without a sound is silently skipped, so hooks can exist before the audio does.
    /// </summary>
    [CreateAssetMenu(fileName = "SFX_", menuName = "Better Together/Audio/Sound Cue")]
    public class SoundCue : ScriptableObject
    {
        [Tooltip("AudioClip or Audio Random Container.")]
        public AudioResource Sound;
        [Tooltip("Optional. When filled, a random clip from this list is played instead of Sound (never the same one twice in a row).")]
        public AudioClip[] Variations;
        [Range(0f, 1f)] public float Volume = 1f;
        [Tooltip("A random pitch in this range is used each time, so repeated sounds don't feel robotic.")]
        public Vector2 PitchRange = new(0.95f, 1.05f);

        [Header("3D")]
        [Tooltip("Heard from where it happens (fades with distance). Off = same volume everywhere, e.g. HUD sounds.")]
        public bool Spatial = true;
        [Tooltip("Distance (m) at which the sound is no longer heard.")]
        public float MaxDistance = 20f;

        [Header("Anti-spam")]
        [Tooltip("Ignore new plays of this cue that happen sooner than this (seconds).")]
        public float MinInterval = 0.05f;

        [System.NonSerialized] internal float LastPlayTime = float.NegativeInfinity;
        [System.NonSerialized] private int _lastVariation = -1;

        /// <summary>True when there is something to play: a sound or at least one variation clip.</summary>
        public bool IsPlayable
        {
            get
            {
                if (Sound != null)
                    return true;

                if (Variations == null)
                    return false;

                foreach (var clip in Variations)
                {
                    if (clip != null)
                        return true;
                }

                return false;
            }
        }

        /// <summary>The resource to play now: a random variation if there are any, otherwise <see cref="Sound"/>.</summary>
        internal AudioResource PickResource()
        {
            var candidates = 0;
            if (Variations != null)
            {
                foreach (var clip in Variations)
                {
                    if (clip != null)
                        candidates++;
                }
            }

            if (candidates == 0)
                return Sound;

            // Random among the non-null clips, skipping the one used last time when there is a choice.
            var avoid = candidates > 1 ? _lastVariation : -1;
            var pick = Random.Range(0, avoid >= 0 ? candidates - 1 : candidates);
            for (var i = 0; i < Variations.Length; i++)
            {
                if (Variations[i] == null || i == avoid)
                    continue;

                if (pick-- == 0)
                {
                    _lastVariation = i;
                    return Variations[i];
                }
            }

            return Sound;
        }
    }

    public static class SoundCueExtensions
    {
        /// <summary>Plays the cue without a position (2D). Safe to call on an unassigned cue.</summary>
        public static void Play(this SoundCue cue) => AudioManager.PlaySfx(cue, null);

        /// <summary>Plays the cue at a world position (3D if the cue is spatial). Safe to call on an unassigned cue.</summary>
        public static void PlayAt(this SoundCue cue, Vector3 position) => AudioManager.PlaySfx(cue, position);
    }
}
