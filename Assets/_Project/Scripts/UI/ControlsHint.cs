using BetterTogether.Diagnostics;
using BetterTogether.Fireplace;
using BetterTogether.Match;
using BetterTogether.Player;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.UI
{
    /// <summary>
    /// Compact list of the local role's controls (bottom right) while the match is playing. A line dims the first time the
    /// player uses that action; the whole list hides after a time limit or when every action was used, and starts over
    /// each match. Purely local: a player joining mid-match sees it from the moment they enter.
    /// </summary>
    public class ControlsHint : MonoBehaviour
    {
        private struct Line
        {
            public ControlLabels.Entry Entry;
            public InputAction Action;
            public bool Used;
        }

        [Tooltip("Object that holds the lines (child of the HUD). It starts disabled.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Fades the whole list out.")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("One text per control line, top to bottom. Needs at least as many as the longest role list.")]
        [SerializeField] private TMP_Text[] _lines;

        [Tooltip("Seconds after the match starts (for this player) before the whole list hides.")]
        [SerializeField] private float _hideAfterSeconds = 60f;

        [Tooltip("Alpha of a line once its action has been used.")]
        [SerializeField, Range(0f, 1f)] private float _usedAlpha = 0.25f;

        [Tooltip("Seconds of the fade when the list hides.")]
        [SerializeField] private float _fadeDuration = 0.5f;

        private Line[] _state;
        private int _count;
        private int _usedCount;
        private bool _active;
        private bool _finished;
        private float _startTime;
        private PlayerRole _builtRole;
        private Tween _fade;
        private bool _throwShown;

        private void Awake()
        {
            _state = new Line[ControlLabels.MaxEntries];
            if (_root != null)
                _root.SetActive(false);
        }

        private void OnDisable()
        {
            _fade.Stop();
            _active = false;
            if (_root != null)
                _root.SetActive(false);
        }

        private void Update()
        {
            if (_root == null || _group == null || _lines == null)
                return;

            // The end screen stands alone: never (re)activate or fade the hint while it is up.
            if (MatchHUD.Instance != null && MatchHUD.Instance.EndScreenShown)
                return;

            var match = MatchManager.Instance;
            if (match == null || match.Phase != MatchPhase.Playing)
            {
                // Lobby, countdown or end screen: hide now and start over on the next match.
                if (_active || _finished)
                    ResetForNextMatch();

                return;
            }

            if (_finished)
                return;

            var local = PlayerCharacter.Local;
            if (local == null)
                return;

            if (!_active)
                Activate(local.Role);
            else if (local.Role != _builtRole)
                Rebuild(local.Role);

            if (Time.unscaledTime - _startTime >= _hideAfterSeconds)
            {
                Finish();
                return;
            }

            DetectUse();
            ShowThrowVerb(local.Role);
        }

        // "Clic · lanzar" only while the gnome holds a water balloon; otherwise the attack line teaches climbing again.
        private void ShowThrowVerb(PlayerRole role)
        {
            var throwing = role == PlayerRole.Gnome && BalloonThrower.Local is { HasBalloon: true };
            if (throwing == _throwShown)
                return;

            _throwShown = throwing;
            for (var i = 0; i < _count; i++)
            {
                if (_state[i].Entry.Name == "attack" && _lines[i] != null)
                    _lines[i].text = throwing ? ControlLabels.Format(_state[i].Entry, ControlLabels.ThrowVerb) : ControlLabels.Format(_state[i].Entry);
            }
        }

        private void Activate(PlayerRole role)
        {
            _fade.Stop();
            _active = true;
            _startTime = Time.unscaledTime;
            _group.alpha = 1f;
            _root.SetActive(true);
            Rebuild(role);
        }

        // Texts are built here (match start or role change), not per frame.
        private void Rebuild(PlayerRole role)
        {
            _builtRole = role;
            _throwShown = false;
            var entries = ControlLabels.For(role);
            _count = Mathf.Min(entries.Length, Mathf.Min(_lines.Length, _state.Length));
            _usedCount = 0;
            for (var i = 0; i < _lines.Length; i++)
            {
                var text = _lines[i];
                if (text == null)
                    continue;

                var show = i < _count;
                text.gameObject.SetActive(show);
                if (!show)
                    continue;

                _state[i] = new Line { Entry = entries[i], Action = ControlLabels.FindAction(entries[i]), Used = false };
                text.text = ControlLabels.Format(entries[i]);
                text.alpha = 1f;
            }
        }

        private void DetectUse()
        {
            // Menu clicks and typing in chat are not gameplay: only count input while the cursor is captured.
            if (Cursor.lockState != CursorLockMode.Locked || FirstPersonCamera.IsTyping)
                return;

            var keyboard = Keyboard.current;
            for (var i = 0; i < _count; i++)
            {
                ref var line = ref _state[i];
                if (line.Used)
                    continue;

                var pressed = line.Action != null
                    ? line.Action.WasPressedThisFrame()
                    : line.Entry.Key != Key.None && keyboard != null && keyboard[line.Entry.Key].wasPressedThisFrame;
                if (!pressed)
                    continue;

                line.Used = true;
                _usedCount++;
                if (_lines[i] != null)
                    _lines[i].alpha = _usedAlpha;

                if (Telemetry.Enabled)
                    Telemetry.Begin("first_action").Str("action", line.Entry.Name).Float("seconds", Time.unscaledTime - _startTime).Send();
            }

            if (_usedCount >= _count)
                Finish();
        }

        // Time limit or everything used: fade out and stay hidden until the next match.
        private void Finish()
        {
            _active = false;
            _finished = true;
            _fade.Stop();
            _fade = Tween.Alpha(_group, _group.alpha, 0f, _fadeDuration, Ease.InQuad, useUnscaledTime: true)
                .OnComplete(this, self => self.CloseIfFinished());
        }

        private void CloseIfFinished()
        {
            if (_finished && _root != null)
                _root.SetActive(false);
        }

        private void ResetForNextMatch()
        {
            _active = false;
            _finished = false;
            _fade.Stop();
            _root.SetActive(false);
        }
    }
}
