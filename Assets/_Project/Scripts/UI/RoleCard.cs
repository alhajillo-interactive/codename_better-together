using System.Text;
using BetterTogether.Diagnostics;
using BetterTogether.Match;
using BetterTogether.Player;
using PrimeTween;
using TMPro;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Big centred card shown during the countdown (and a few seconds after the match starts) that tells the local player
    /// what they are, what to do and which keys to use. Purely local and driven by the replicated match phase: nothing is
    /// sent over the network, and a player who joins when the match is already playing never sees it.
    /// </summary>
    public class RoleCard : MonoBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Card object (child of the HUD). It starts disabled and is enabled while the card is on screen.")]
        [SerializeField] private GameObject _card;

        [Tooltip("Fades the card in and out.")]
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _objective;
        [SerializeField] private TMP_Text _controls;

        [Header("Timing")]
        [Tooltip("Seconds the card stays after the match starts.")]
        [SerializeField] private float _holdAfterStart = 3f;

        [Tooltip("Seconds of the entrance animation (fade and pop).")]
        [SerializeField] private float _enterDuration = 0.45f;

        [Tooltip("Seconds of the exit fade.")]
        [SerializeField] private float _exitDuration = 0.35f;

        [Tooltip("Scale the card pops in from.")]
        [SerializeField] private float _enterScale = 0.85f;

        [Header("Texts (shown to the player)")]
        [SerializeField] private string _camperTitle = "Eres el CAMPISTA";
        [SerializeField] private string _camperObjective = "1 Leños → 2 Fósforos → 3 Salchicha";
        [SerializeField] private string _gnomeTitle = "Eres un GNOMO";
        [SerializeField] private string _gnomeObjective = "Roba, esconde y apaga la fogata";

        private readonly StringBuilder _builder = new();

        private bool _shownThisMatch;
        private bool _visible;
        private bool _hasPlayingStart;
        private float _playingStart;
        private PlayerRole _builtRole;
        private Sequence _sequence;
        private Tween _fade;

        private void Awake()
        {
            if (_card != null)
                _card.SetActive(false);
        }

        private void OnDisable()
        {
            _sequence.Stop();
            _fade.Stop();
            _visible = false;
            if (_card != null)
                _card.SetActive(false);
        }

        private void Update()
        {
            if (_card == null || _group == null)
                return;

            var match = MatchManager.Instance;
            var phase = match != null ? match.Phase : MatchPhase.Waiting;
            var local = PlayerCharacter.Local;

            switch (phase)
            {
                case MatchPhase.Countdown:
                    _hasPlayingStart = false;
                    if (local == null)
                        break;

                    if (!_shownThisMatch)
                        Show(local.Role);
                    else if (_visible && local.Role != _builtRole)
                        BuildTexts(local.Role);
                    break;

                case MatchPhase.Playing:
                    if (!_visible)
                        break;

                    if (!_hasPlayingStart)
                    {
                        _hasPlayingStart = true;
                        _playingStart = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - _playingStart >= _holdAfterStart)
                    {
                        Hide(_exitDuration);
                    }

                    break;

                default:
                    // Waiting or Ended: the next countdown shows the card again.
                    _shownThisMatch = false;
                    _hasPlayingStart = false;
                    if (_visible)
                        Hide(0f);
                    break;
            }
        }

        private void Show(PlayerRole role)
        {
            _shownThisMatch = true;
            _visible = true;
            BuildTexts(role);

            _sequence.Stop();
            _fade.Stop();
            _card.SetActive(true);
            _group.alpha = 0f;
            var rect = (RectTransform)_card.transform;
            rect.localScale = Vector3.one * _enterScale;

            _sequence = Sequence.Create(useUnscaledTime: true)
                .Group(Tween.Alpha(_group, 0f, 1f, _enterDuration, Ease.OutQuad, useUnscaledTime: true))
                .Group(Tween.Scale(rect, _enterScale, 1f, _enterDuration, Ease.OutBack, useUnscaledTime: true));

            if (Telemetry.Enabled)
                Telemetry.Begin("role_card_shown").Str("card_role", role == PlayerRole.Camper ? "camper" : "gnome").Send();
        }

        private void Hide(float duration)
        {
            _visible = false;
            _sequence.Stop();
            _fade.Stop();

            if (duration <= 0f)
            {
                _card.SetActive(false);
                return;
            }

            _fade = Tween.Alpha(_group, _group.alpha, 0f, duration, Ease.InQuad, useUnscaledTime: true)
                .OnComplete(this, self => self.CloseIfHidden());
        }

        private void CloseIfHidden()
        {
            // A new countdown can have started while fading out.
            if (!_visible && _card != null)
                _card.SetActive(false);
        }

        // Built once per show (and when the role changes during the countdown), never per frame.
        private void BuildTexts(PlayerRole role)
        {
            _builtRole = role;
            var camper = role == PlayerRole.Camper;
            if (_title != null)
                _title.text = camper ? _camperTitle : _gnomeTitle;

            if (_objective != null)
                _objective.text = camper ? _camperObjective : _gnomeObjective;

            if (_controls != null)
            {
                _builder.Clear();
                ControlLabels.AppendAll(_builder, role, "  ·  ");
                _controls.text = _builder.ToString();
            }
        }
    }
}
