using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// A piece of music. Create with Create > Better Together > Audio > Music Track and play it with
    /// <see cref="AudioManager.PlayMusic"/>; switching tracks crossfades automatically.
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
