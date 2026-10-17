using BetterTogether.Player;
using Fusion;
using UnityEngine;

namespace BetterTogether.Voice
{
    /// <summary>
    /// What the camper hears instead of a gnome's real voice: gibberish "mi" sounds while the gnome talks.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GnomeChatter : NetworkBehaviour
    {
        [SerializeField] private PlayerVoice _voice;
        [SerializeField] private AudioClip[] _clips;
        [SerializeField] private Vector2 _pitchRange = new(0.9f, 1.3f);
        [SerializeField] private Vector2 _intervalRange = new(0.12f, 0.25f);

        private AudioSource _audio;
        private float _nextChirpTime;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
        }

        public override void Render()
        {
            var local = PlayerCharacter.Local;
            if (local == null || local.Role != PlayerRole.Camper || !_voice.IsTalking || _clips.Length == 0)
                return;

            if (Time.time < _nextChirpTime)
                return;

            var clip = _clips[Random.Range(0, _clips.Length)];
            _audio.pitch = Random.Range(_pitchRange.x, _pitchRange.y);
            _audio.PlayOneShot(clip);
            _nextChirpTime = Time.time + Random.Range(_intervalRange.x, _intervalRange.y) + clip.length / _audio.pitch;
        }
    }
}
