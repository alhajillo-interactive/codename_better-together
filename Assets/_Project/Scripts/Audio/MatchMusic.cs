using BetterTogether.Match;
using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Follows the phase of the <see cref="MatchManager"/> on the same object: lobby (waiting and countdown),
    /// playing and ended each have their own track. It reads the networked state every frame, so players who join
    /// mid-match get the right music too. Leave a track empty for silence in that state.
    /// </summary>
    [RequireComponent(typeof(MatchManager))]
    public class MatchMusic : MonoBehaviour
    {
        [SerializeField] private MusicTrack _lobby;
        [SerializeField] private MusicTrack _playing;
        [Tooltip("Short ending piece; set it to not loop.")]
        [SerializeField] private MusicTrack _ended;

        private MatchManager _match;
        private MatchPhase? _shownTrackPhase;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void Update()
        {
            if (_match.Object == null || !_match.Object.IsValid)
                return;

            var phase = _match.Phase;
            if (phase == MatchPhase.Countdown)
                phase = MatchPhase.Waiting;

            if (phase == _shownTrackPhase)
                return;

            _shownTrackPhase = phase;
            AudioManager.PlayMusic(phase switch
            {
                MatchPhase.Waiting => _lobby,
                MatchPhase.Playing => _playing,
                _ => _ended,
            });
        }

        private void OnDestroy()
        {
            // Leaving the session ends its music.
            if (_shownTrackPhase.HasValue)
                AudioManager.StopMusic();
        }
    }
}
