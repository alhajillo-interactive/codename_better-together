using BetterTogether.Match;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Follows the phase of the <see cref="MatchManager"/> on the same object: lobby (waiting and countdown),
    /// playing and ended each have their own track (point them all to the same one for a song that never restarts).
    /// It reads the networked state every frame, so players who join mid-match get the right music too.
    /// Leave a track empty for silence in that state. When a match ends it also plays a win or lose stinger,
    /// depending on the local player's side.
    /// </summary>
    [RequireComponent(typeof(MatchManager))]
    public class MatchMusic : MonoBehaviour
    {
        [SerializeField] private MusicTrack _lobby;
        [SerializeField] private MusicTrack _playing;
        [Tooltip("Optional. Plays instead of the playing track while the fire is lit (milestone 2, defending it); empty keeps the playing track.")]
        [SerializeField] private MusicTrack _playingDefend;
        [Tooltip("Short ending piece; set it to not loop.")]
        [SerializeField] private MusicTrack _ended;

        [Header("Result")]
        [Tooltip("Played when the match ends and the local player's side won. Use a non-Spatial cue.")]
        [SerializeField] private SoundCue _winSfx;
        [Tooltip("Played when the match ends and the local player's side lost. Use a non-Spatial cue.")]
        [SerializeField] private SoundCue _loseSfx;

        private MatchManager _match;
        private MatchPhase? _shownTrackPhase;
        private MatchPhase? _lastPhase;
        private bool _shownDefend;
        private bool _resultPending;

        private void Awake() => _match = GetComponent<MatchManager>();

        private void Update()
        {
            if (_match.Object == null || !_match.Object.IsValid)
                return;

            var phase = _match.Phase;
            UpdateResultSound(phase);

            if (phase == MatchPhase.Countdown)
                phase = MatchPhase.Waiting;

            // The milestone is derived from the lit fire, so late joiners and a new master hear the same track.
            var defend = phase == MatchPhase.Playing && _playingDefend != null && MatchStage.Current == MatchStage.Stage.DefendFire;
            if (phase == _shownTrackPhase && defend == _shownDefend)
                return;

            _shownTrackPhase = phase;
            _shownDefend = defend;
            AudioManager.PlayMusic(phase switch
            {
                MatchPhase.Waiting => _lobby,
                MatchPhase.Playing => defend ? _playingDefend : _playing,
                _ => _ended,
            });
        }

        private void OnDestroy()
        {
            // Leaving the session goes back to the menu music.
            if (_shownTrackPhase.HasValue)
                AudioManager.PlayMusic(_lobby);
        }

        // Only a transition into Ended seen by this client counts, so late joiners don't hear a stale result.
        // The result may arrive a tick after the phase, so it waits for it.
        private void UpdateResultSound(MatchPhase phase)
        {
            if (phase != _lastPhase)
            {
                _resultPending = phase == MatchPhase.Ended && _lastPhase.HasValue;
                _lastPhase = phase;
            }

            if (!_resultPending)
                return;

            var result = _match.Result;
            if (result == MatchResult.None)
                return;

            _resultPending = false;
            var local = PlayerCharacter.Local;
            if (local == null)
                return;

            var won = local.Role == PlayerRole.Camper ? result == MatchResult.CamperWins : result == MatchResult.GnomesWin;
            (won ? _winSfx : _loseSfx).Play();
        }
    }
}
