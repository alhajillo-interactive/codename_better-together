using BetterTogether.Diagnostics;
using BetterTogether.Match;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Optional four-question survey shown a few seconds into the end screen (card C3.3). Only works in the Editor and
    /// development builds, and only while a telemetry match file is open: the answers become one <c>survey</c> row.
    /// Purely local presentation: nothing is replicated, so late joiners, master changes and players that leave are
    /// unaffected (a peer that was not playing simply never sees the survey). The panel closes by itself when the end
    /// screen is over; a skipped survey writes nothing. Built by <c>GnoMore/Construir encuesta</c>.
    /// </summary>
    public class PostMatchSurvey : MonoBehaviour
    {
        private const int MaxMomentLength = 140;

        [Header("References (wired by SurveyBuilder)")]
        [Tooltip("Panel that holds the questions; hidden until the survey is due.")]
        [SerializeField] private GameObject _panel;
        [Tooltip("Buttons for 'how fun was it', in order from 1 to 5.")]
        [SerializeField] private Button[] _funButtons;
        [Tooltip("Dizzy: yes.")]
        [SerializeField] private Button _dizzyYes;
        [Tooltip("Dizzy: no.")]
        [SerializeField] private Button _dizzyNo;
        [Tooltip("Understood the objective: yes.")]
        [SerializeField] private Button _understoodYes;
        [Tooltip("Understood the objective: no.")]
        [SerializeField] private Button _understoodNo;
        [Tooltip("Free text for the memorable moment (limited to 140 characters).")]
        [SerializeField] private TMP_InputField _moment;
        [Tooltip("Sends the answers given so far.")]
        [SerializeField] private Button _send;
        [Tooltip("Closes the survey without sending anything.")]
        [SerializeField] private Button _skip;

        [Header("Behaviour")]
        [Tooltip("Seconds after the end screen appears before the survey is shown.")]
        [Min(0f)] [SerializeField] private float _showDelay = 1.5f;
        [Tooltip("Tint of the answers that are not selected.")]
        [SerializeField] private Color _idleColor = new(1f, 1f, 1f, 1f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text

        // Chosen option tint: leafDark from the theme (semantic: confirm).
        private Color SelectedColor => UiTheme.Safe.leafDark;

        private bool _valid;
        private bool _open;
        private bool _shownThisEnd;
        private bool _wasEnded;
        private float _endedAt;
        private int _fun;          // 0 = not answered
        private int _dizzy = -1;   // -1 = not answered, 0 = no, 1 = yes
        private int _understood = -1;

        private void Awake()
        {
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }

            _valid = HudSupport.Require(this, _panel, nameof(_panel))
                     & HudSupport.Require(this, _dizzyYes, nameof(_dizzyYes))
                     & HudSupport.Require(this, _dizzyNo, nameof(_dizzyNo))
                     & HudSupport.Require(this, _understoodYes, nameof(_understoodYes))
                     & HudSupport.Require(this, _understoodNo, nameof(_understoodNo))
                     & HudSupport.Require(this, _moment, nameof(_moment))
                     & HudSupport.Require(this, _send, nameof(_send))
                     & HudSupport.Require(this, _skip, nameof(_skip));
            if (_funButtons == null || _funButtons.Length != 5)
            {
                Debug.LogError($"[{nameof(PostMatchSurvey)}] '_funButtons' must hold exactly 5 buttons.", this);
                _valid = false;
            }

            if (!_valid)
            {
                enabled = false;
                return;
            }

            for (var i = 0; i < _funButtons.Length; i++)
            {
                var value = i + 1;
                _funButtons[i].onClick.AddListener(() => { _fun = value; Refresh(); });
            }

            _dizzyYes.onClick.AddListener(() => { _dizzy = 1; Refresh(); });
            _dizzyNo.onClick.AddListener(() => { _dizzy = 0; Refresh(); });
            _understoodYes.onClick.AddListener(() => { _understood = 1; Refresh(); });
            _understoodNo.onClick.AddListener(() => { _understood = 0; Refresh(); });
            _send.onClick.AddListener(Send);
            _skip.onClick.AddListener(Close);
            _moment.characterLimit = MaxMomentLength;
            _moment.onSelect.AddListener(_ => FirstPersonCamera.UiTyping = true);
            _moment.onDeselect.AddListener(_ => FirstPersonCamera.UiTyping = false);
            _panel.SetActive(false);
        }

        private void OnDisable()
        {
            if (_open)
                Close();
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            var ended = match != null && match.Phase == MatchPhase.Ended;
            if (ended != _wasEnded)
            {
                _wasEnded = ended;
                if (ended)
                {
                    _endedAt = Time.unscaledTime;
                    _shownThisEnd = false;
                }
                else if (_open)
                {
                    Close(); // End screen over: the match file is about to close.
                }
            }

            if (ended && !_open && !_shownThisEnd && Telemetry.Enabled && PlayerCharacter.Local != null
                && Time.unscaledTime - _endedAt >= _showDelay)
                Open();
        }

        private void Open()
        {
            _open = true;
            _shownThisEnd = true; // One showing per end screen, even if skipped.
            _fun = 0;
            _dizzy = -1;
            _understood = -1;
            _moment.text = string.Empty;
            Refresh();
            _panel.SetActive(true);
            FirstPersonCamera.CursorFree = true; // Releases the mouse; the camera re-locks it when this goes back to false.
        }

        private void Close()
        {
            _open = false;
            _panel.SetActive(false);
            _moment.DeactivateInputField();
            FirstPersonCamera.UiTyping = false;
            FirstPersonCamera.CursorFree = false;
        }

        private void Send()
        {
            var hasText = !string.IsNullOrWhiteSpace(_moment.text);
            if (Telemetry.Enabled && (_fun > 0 || _dizzy >= 0 || _understood >= 0 || hasText))
            {
                // The row's role column is the role the local player had in this match.
                var row = Telemetry.Begin("survey");
                if (_fun > 0)
                    row = row.Int("fun", _fun);
                if (_dizzy >= 0)
                    row = row.Bool("dizzy", _dizzy == 1);
                if (_understood >= 0)
                    row = row.Bool("understood", _understood == 1);
                if (hasText)
                    row = row.Str("moment", _moment.text.Trim());
                row.Send();
            }

            Close();
        }

        // Tints the selected answers; runs on a click only.
        private void Refresh()
        {
            for (var i = 0; i < _funButtons.Length; i++)
                Tint(_funButtons[i], _fun == i + 1);

            Tint(_dizzyYes, _dizzy == 1);
            Tint(_dizzyNo, _dizzy == 0);
            Tint(_understoodYes, _understood == 1);
            Tint(_understoodNo, _understood == 0);
        }

        private void Tint(Button button, bool selected)
        {
            if (button.targetGraphic != null)
                button.targetGraphic.color = selected ? SelectedColor : _idleColor;
        }
    }
}
