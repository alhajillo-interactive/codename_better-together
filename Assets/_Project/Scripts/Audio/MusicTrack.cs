using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// A long looping piece: music (<see cref="AudioManager.PlayMusic"/>) or a background ambience bed
    /// (<see cref="AudioManager.PlayAmbience"/>). Create with Create > Better Together > Audio > Music Track;
    /// switching tracks crossfades automatically.
    /// </summary>
    [CreateAssetMenu(fileName = "Music_", menuName = "Better Together/Audio/Music Track")]
    public class MusicTrack : ScriptableObject
    {
        public AudioClip Clip;
        [Tooltip("Relative volume of this track, before the player's music volume setting.")]
        [Range(0f, 1f)] public float Volume = 1f;
        public bool Loop = true;
    }
}
