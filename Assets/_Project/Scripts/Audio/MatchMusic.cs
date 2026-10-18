using UnityEngine;

namespace BetterTogether.Audio
{
    /// <summary>
    /// Follows the match state of the <see cref="GameManager"/> on the same object: lobby, playing and ended each
    /// have their own track. It reads the networked state every frame, so players who join mid-match get the
    /// right music too. Leave a track empty for silence in that state.
    /// </summary>
    [RequireComponent(typeof(GameManager))]
    public class MatchMusic : MonoBehaviour
    {
        [SerializeField] private MusicTrack _lobby;
        [SerializeField] private MusicTrack _playing;
        [Tooltip("Short ending piece; set it to not loop.")]
        [SerializeField] private MusicTrack _ended;

        private GameManager _gameManager;
        private GameManager.PlayState? _shownState;

        private void Awake() => _gameManager = GetComponent<GameManager>();

        private void Update()
        {
            if (_gameManager.Object == null || !_gameManager.Object.IsValid)
                return;

            var state = _gameManager.CurrentState;
            if (state == _shownState)
                return;

            _shownState = state;
            AudioManager.PlayMusic(state switch
            {
                GameManager.PlayState.Lobby => _lobby,
                GameManager.PlayState.Playing => _playing,
                _ => _ended,
            });
        }

        private void OnDestroy()
        {
            // Leaving the session ends its music.
            if (_shownState.HasValue)
                AudioManager.StopMusic();
        }
    }
}
