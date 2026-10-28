using System.Collections.Generic;
using BetterTogether.Environment;
using BetterTogether.Fireplace;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Match overlay: match clock, camper objectives, announcer, waiting notice and end screen.
    /// Purely local: it polls the shared state every frame and reacts to transitions, so it works
    /// even when the match manager is missing (then only the clock and objectives show).
    /// </summary>
    public class MatchHUD : MonoBehaviour
    {
        private const int ObjectiveCount = 4;
        private const int MaxQueued = 3;
        private const float DayCycleSearchInterval = 1f;

        private struct Announcement
        {
            public string Text;
            public Color Color;
            public float Hold;
        }

        private sealed class Line
        {
            public TMP_Text Text;
            public UIPunch Punch;
            public bool Done;
            public bool Known;
            public int Key = int.MinValue;
            public float RedUntil;
        }

        [Header("Clock")]
        [SerializeField] private RectTransform _clock;
        [SerializeField] private RectTransform _track;
        [SerializeField] private RectTransform _sun;
        [SerializeField] private Image _sunImage;
        [SerializeField] private TMP_Text _timeLabel;
        [SerializeField] private Color _sunColor = new(1f, 0.85f, 0.3f);
        [SerializeField] private Color _moonColor = new(0.7f, 0.8f, 1f);
        [SerializeField] private Color _urgentColor = new(1f, 0.25f, 0.2f);
        [SerializeField] private float _urgentSeconds = 10f;

        [Header("Objectives")]
        [SerializeField] private TMP_Text _objectivesTitle;
        [SerializeField] private TMP_Text[] _objectiveTexts;
        [SerializeField] private Color _pendingColor = Color.white;
        [SerializeField] private Color _doneColor = new(0.45f, 0.9f, 0.45f);
        [SerializeField] private Color _failColor = new(1f, 0.3f, 0.25f);
        [Tooltip("Colour of the greyed-out line that previews the next milestone.")]
        [SerializeField] private Color _mutedColor = new(0.6f, 0.6f, 0.6f);

        [Header("Announcer")]
        [SerializeField] private TMP_Text _announcer;
        [SerializeField] private float _holdTime = 1.1f;
        [SerializeField] private float _popDuration = 0.35f;
        [SerializeField] private float _fadeDuration = 0.3f;

        [Header("Waiting")]
        [SerializeField] private TMP_Text _waiting;

        [Header("End screen")]
        [SerializeField] private GameObject _endScreen;
        [SerializeField] private CanvasGroup _endGroup;
        [SerializeField] private TMP_Text _endTitle;
        [SerializeField] private TMP_Text _endSubtitle;
        [SerializeField] private TMP_Text _endRematch;
        [SerializeField] private float _endFadeDuration = 0.4f;

        private static readonly Dictionary<string, string> StruckCache = new();

        private readonly Queue<Announcement> _queue = new();
        private Line[] _lines;
        private DayNightCycle _dayCycle;
        private float _nextDaySearch;
        private UIPunch _clockPunch;
        private UIPunch _announcerPunch;
        private UIPunch _titlePunch;

        private Announcement _current;
        private bool _hasCurrent;
        private float _currentElapsed;

        private MatchPhase _lastPhase = (MatchPhase)(-1);
        private int _lastCountdown = -1;
        private int _lastTickSecond = -1;
        private float _prevSecondsUntilNight = -1f;
        private float _prevRemaining = -1f;
        private bool _hasFireState;
        private bool _prevLit;
        private bool _prevDoused;
        private float _nextDouseWarning;
        private int _titleVariant = -1;
        private bool _hasSausageState;
        private Sausage.CookState _prevCook;
        private bool _endShown;
        private float _endElapsed;
        private int _lastRematch = -1;
        private int _lastWaitingKey = -1;
        private int _lastClockSecond = -1;

        private void Awake()
        {
            _clockPunch = UIPunch.Of(_clock);
            _announcerPunch = UIPunch.Of(_announcer);
            _titlePunch = UIPunch.Of(_endTitle);

            _lines = new Line[ObjectiveCount];
            for (var i = 0; i < ObjectiveCount; i++)
                _lines[i] = new Line { Text = _objectiveTexts[i], Punch = UIPunch.Of(_objectiveTexts[i]) };

            _announcer.alpha = 0f;
            _waiting.gameObject.SetActive(false);
            _endScreen.SetActive(false);
        }

        private void Update()
        {
            FindDayCycle();

            var match = MatchManager.Instance;
            var phase = match != null ? match.Phase : MatchPhase.Waiting;
            var playing = phase == MatchPhase.Playing;

            UpdateClock(match, phase, playing);
            UpdateObjectives(playing);
            DetectPhase(match, phase);
            DetectNight(match, playing);
            DetectFire(playing);
            DetectSausage(playing);
            UpdateWaiting(match, phase);
            UpdateEndScreen(match, phase);
            UpdateAnnouncer();
        }

        private void FindDayCycle()
        {
            if (_dayCycle != null || Time.unscaledTime < _nextDaySearch)
                return;

            _dayCycle = FindAnyObjectByType<DayNightCycle>();
            _nextDaySearch = Time.unscaledTime + DayCycleSearchInterval;
        }

        #region Clock

        /// <summary>Seconds on the clock: the match timer when there is a match, otherwise the daylight left.</summary>
        private float ClockSeconds(MatchManager match, MatchPhase phase)
        {
            if (match == null)
                return _dayCycle.SecondsUntilNight;

            return phase switch
            {
                MatchPhase.Playing => match.TimeRemaining,
                MatchPhase.Ended => 0f,
                _ => match.MatchDuration,
            };
        }

        private void UpdateClock(MatchManager match, MatchPhase phase, bool playing)
        {
            var visible = _dayCycle != null;
            if (_clock.gameObject.activeSelf != visible)
                _clock.gameObject.SetActive(visible);

            if (!visible)
                return;

            var progress = Mathf.Clamp01(_dayCycle.DayProgress01);
            var half = _track.rect.width * 0.5f;
            _sun.anchoredPosition = new Vector2(Mathf.Lerp(-half, half, progress), 0f);

            var night = _dayCycle.IsNight;
            _sunImage.color = night ? _moonColor : _sunColor;

            var seconds = Mathf.Max(0f, ClockSeconds(match, phase));
            var whole = Mathf.CeilToInt(seconds);
            var urgent = seconds <= _urgentSeconds && match != null && phase == MatchPhase.Playing;
            _timeLabel.color = urgent ? _urgentColor : night ? _moonColor : Color.white;

            if (whole != _lastClockSecond)
            {
                _lastClockSecond = whole;
                _timeLabel.text = $"{whole / 60}:{whole % 60:00}";
            }

            if (!urgent)
            {
                _lastTickSecond = -1;
                return;
            }

            if (whole != _lastTickSecond)
            {
                _lastTickSecond = whole;
                if (playing)
                    _clockPunch.Punch(1.2f, 0.25f);
            }
        }

        #endregion

        #region Objectives

        // Four lines per role and milestone (see MatchStage); the variant is part of every line's key, so a change
        // of role or milestone rebuilds all of them. Texts are literals or SetText formats: nothing allocates per frame.
        private void UpdateObjectives(bool playing)
        {
            var local = PlayerCharacter.Local;
            var camper = local == null || local.Role == PlayerRole.Camper;
            var defend = MatchStage.Current == MatchStage.Stage.DefendFire;
            var variant = (camper ? 0 : 2) + (defend ? 1 : 0);
            SetTitle(variant);

            var fire = FireplaceManager.Instance;
            var sausage = Sausage.Instance;
            var wood = fire != null ? fire.WoodCount : 0;
            var required = fire != null ? fire.RequiredWood : 0;
            var cook = sausage != null ? sausage.State : Sausage.CookState.Raw;
            var eaten = sausage != null && sausage.Eaten;
            var cookDone = sausage != null && (eaten || cook == Sausage.CookState.Cooked);
            var sausageText = cook switch
            {
                Sausage.CookState.Cooked => "Salchicha: Lista",
                Sausage.CookState.Burnt => "Salchicha: Quemada",
                _ => "Salchicha: Cruda",
            };

            switch (variant)
            {
                case 0: // Camper, milestone 1: build and light the fire.
                    var carried = PlayerInteractor.Local != null ? PlayerInteractor.Local.Carried : null;
                    var hasMatches = carried != null && carried.TryGetComponent<Matches>(out _);
                    ShowCount(0, variant, fire != null && wood >= required, false, "Leños {0:0}/{1:0}", wood, required, playing);
                    ShowPlain(1, variant, hasMatches, false, "Consigue los fósforos", 0, playing);
                    ShowPlain(2, variant, false, false, "Enciende la fogata", 0, playing);
                    ShowPlain(3, variant, false, true, "(Luego: cocina y come la salchicha)", 0, playing);
                    break;
                case 1: // Camper, milestone 2: defend the fire while the sausage cooks.
                    ShowPlain(0, variant, false, false, "¡Defiende la fogata!", 0, playing);
                    ShowPlain(1, variant, cookDone, false, sausageText, (int)cook, playing);
                    ShowPlain(2, variant, eaten, false, "Cómela cuando esté lista", 0, playing);
                    ShowPlain(3, variant, false, false, "Patea a quien traiga agua", 0, playing);
                    break;
                case 2: // Gnome, milestone 1: steal and hide.
                    ShowPlain(0, variant, false, false, "Roba leños y fósforos", 0, playing);
                    ShowPlain(1, variant, false, false, "Escóndelos", 0, playing);
                    ShowCount(2, variant, false, false, "Leños del campista {0:0}/{1:0}", wood, required, playing);
                    ShowPlain(3, variant, false, true, "(Si la enciende: busca la cubeta)", 0, playing);
                    break;
                default: // Gnome, milestone 2: bring the water.
                    var held = PlayerInteractor.Local != null ? PlayerInteractor.Local.Carried : null;
                    var bucketState = 0;
                    if (held != null && held.TryGetComponent<WaterBucket>(out var bucket))
                        bucketState = bucket.IsFull ? 2 : 1;

                    var bucketText = bucketState switch
                    {
                        2 => "¡Llévala a la fogata!",
                        1 => "Llénala en el río",
                        _ => "Busca la cubeta",
                    };

                    ShowPlain(0, variant, false, false, "¡Apaga la fogata!", 0, playing);
                    ShowPlain(1, variant, false, false, bucketText, bucketState, playing);
                    ShowPlain(2, variant, false, false, sausageText, (int)cook, playing);
                    ShowPlain(3, variant, false, false, "Que no se la coma", 0, playing);
                    break;
            }
        }

        private void SetTitle(int variant)
        {
            if (variant == _titleVariant)
                return;

            _titleVariant = variant;
            _objectivesTitle.SetText(variant switch
            {
                0 => "Hito 1 · Arma la fogata",
                1 => "Hito 2 · Defiéndela",
                2 => "Hito 1 · Sabotea al campista",
                _ => "Hito 2 · ¡Ataca la fogata!",
            });
        }

        // A line whose text is a literal; extra tells apart the literals a line can show in one variant.
        private void ShowPlain(int index, int variant, bool done, bool muted, string text, int extra, bool playing)
        {
            if (UpdateLine(index, done, variant * 16 + extra, playing, muted))
                _lines[index].Text.SetText(done ? Struck(text) : text);
        }

        // A line with two numbers, written with SetText so no string is built.
        private void ShowCount(int index, int variant, bool done, bool muted, string format, int a, int b, bool playing)
        {
            if (UpdateLine(index, done, variant * 16 + (a * 100 + b) * 64, playing, muted))
                _lines[index].Text.SetText(done ? Struck(format) : format, a, b);
        }

        // Strikethrough version of a literal; built once per literal.
        private static string Struck(string text)
        {
            if (!StruckCache.TryGetValue(text, out var struck))
            {
                struck = "<s>" + text + "</s>";
                StruckCache.Add(text, struck);
            }

            return struck;
        }

        /// <summary>Plays the completion or failure juice and returns true when the line text must be rebuilt.</summary>
        private bool UpdateLine(int index, bool done, int key, bool playing, bool muted)
        {
            var line = _lines[index];

            if (line.Known && done != line.Done && playing)
            {
                if (done)
                {
                    line.Punch.Punch(1.25f, 0.25f);
                }
                else
                {
                    line.Punch.Shake(10f, 0.4f);
                    line.RedUntil = Time.unscaledTime + 0.5f;
                }
            }

            line.Known = true;
            line.Done = done;

            var failing = Time.unscaledTime < line.RedUntil;
            var color = failing ? _failColor : muted ? _mutedColor : done ? _doneColor : _pendingColor;
            if (line.Text.color != color)
                line.Text.color = color;

            var fullKey = key * 2 + (done ? 1 : 0);
            if (fullKey == line.Key)
                return false;

            line.Key = fullKey;
            return true;
        }

        #endregion

        #region Announcements

        private void DetectPhase(MatchManager match, MatchPhase phase)
        {
            if (phase != _lastPhase)
            {
                if (_lastPhase == MatchPhase.Countdown && phase == MatchPhase.Playing)
                    Announce("¡A jugar!", Color.white, true);

                _lastPhase = phase;
                _lastCountdown = -1;
            }

            if (match == null || phase != MatchPhase.Countdown)
                return;

            var remaining = Mathf.CeilToInt(match.PhaseTimeRemaining);
            if (remaining == _lastCountdown || remaining <= 0)
                return;

            _lastCountdown = remaining;
            Announce(remaining.ToString(), new Color(1f, 0.92f, 0.5f), true, 0.7f);
        }

        private void DetectNight(MatchManager match, bool playing)
        {
            DetectNightfall(playing);
            DetectLastSeconds(match, playing);
        }

        private void DetectNightfall(bool playing)
        {
            if (_dayCycle == null || _dayCycle.IsNight)
            {
                _prevSecondsUntilNight = -1f;
                return;
            }

            var now = _dayCycle.SecondsUntilNight;
            var previous = _prevSecondsUntilNight;
            _prevSecondsUntilNight = now;

            if (previous > 30f && now <= 30f && playing)
                Announce("¡Se acerca la noche!", new Color(0.6f, 0.7f, 1f));
        }

        private void DetectLastSeconds(MatchManager match, bool playing)
        {
            if (match == null || !playing)
            {
                _prevRemaining = -1f;
                return;
            }

            var now = match.TimeRemaining;
            var previous = _prevRemaining;
            _prevRemaining = now;

            if (previous > _urgentSeconds && now <= _urgentSeconds)
            {
                Announce($"¡{Mathf.CeilToInt(_urgentSeconds)} segundos!", _urgentColor);
                Flash(new Color(0.2f, 0.3f, 0.8f, 0.15f), 0.4f);
            }
        }

        // The change of milestone: the fire being lit starts milestone 2, putting it out brings milestone 1 back.
        // Texts differ per role, since the roles invert.
        private void DetectFire(bool playing)
        {
            var fire = FireplaceManager.Instance;
            if (fire == null)
            {
                _hasFireState = false;
                return;
            }

            var camper = PlayerCharacter.Local == null || PlayerCharacter.Local.Role == PlayerRole.Camper;
            var lit = fire.IsLit;
            if (_hasFireState && playing && lit != _prevLit)
            {
                if (lit)
                {
                    Announce(camper ? "¡Fuego! Defiéndelo mientras se cocina" : "¡FUEGO! Ahora los duendes atacan",
                        new Color(1f, 0.7f, 0.3f), true, 1.6f);
                    Flash(new Color(1f, 0.6f, 0.2f, 0.2f), 0.4f);
                    if (CameraPunch.Instance != null)
                        CameraPunch.Instance.PunchFov(5f, 0.4f);
                }
                else
                {
                    Announce(camper ? "¡Apagada! Que no la vuelva a prender" : "¡Apagada! Vuelve a encenderla", _failColor, true, 1.6f);
                    Flash(new Color(0.3f, 0.5f, 1f, 0.25f), 0.4f);
                }
            }

            // Warn the camper when the water starts to pour (not again right away if a pour is interrupted and restarted).
            var doused = fire.IsBeingDoused;
            if (_hasFireState && playing && doused && !_prevDoused && Time.unscaledTime >= _nextDouseWarning && camper)
            {
                Announce("¡Te apagan la fogata!", _failColor);
                _nextDouseWarning = Time.unscaledTime + 5f;
            }

            _prevDoused = doused;
            _prevLit = lit;
            _hasFireState = true;
        }

        private void DetectSausage(bool playing)
        {
            var sausage = Sausage.Instance;
            if (sausage == null)
            {
                _hasSausageState = false;
                return;
            }

            var state = sausage.State;
            if (_hasSausageState && playing && state != _prevCook)
            {
                if (state == Sausage.CookState.Cooked)
                    Announce("¡La salchicha está lista!", _doneColor);
                else if (state == Sausage.CookState.Burnt)
                    Announce("¡Se quemó la salchicha!", _failColor);
            }

            _prevCook = state;
            _hasSausageState = true;
        }

        private void Announce(string text, Color color, bool interrupt = false, float hold = -1f)
        {
            var announcement = new Announcement
            {
                Text = text,
                Color = color,
                Hold = hold > 0f ? hold : _holdTime,
            };

            if (interrupt)
            {
                _queue.Clear();
                Show(announcement);
                return;
            }

            if (_queue.Count >= MaxQueued)
                _queue.Dequeue();

            _queue.Enqueue(announcement);
        }

        private void Show(Announcement announcement)
        {
            _current = announcement;
            _hasCurrent = true;
            _currentElapsed = 0f;
            _announcer.text = announcement.Text;
            _announcer.color = announcement.Color;
            _announcerPunch.Pop(0.6f, 1.15f, _popDuration);
        }

        private void UpdateAnnouncer()
        {
            if (_hasCurrent)
            {
                _currentElapsed += Time.unscaledDeltaTime;
                var fadeStart = _popDuration + _current.Hold;
                var alpha = _currentElapsed < fadeStart
                    ? Mathf.Clamp01(_currentElapsed / 0.1f)
                    : 1f - (_currentElapsed - fadeStart) / _fadeDuration;

                _announcer.alpha = Mathf.Clamp01(alpha);
                if (_currentElapsed >= fadeStart + _fadeDuration)
                {
                    _hasCurrent = false;
                    _announcer.alpha = 0f;
                }
            }

            if (!_hasCurrent && _queue.Count > 0)
                Show(_queue.Dequeue());
        }

        private static void Flash(Color color, float duration)
        {
            if (ScreenFlash.Instance != null)
                ScreenFlash.Instance.Flash(color, duration);
        }

        #endregion

        #region Waiting and end screen

        private void UpdateWaiting(MatchManager match, MatchPhase phase)
        {
            var show = match != null && phase == MatchPhase.Waiting;
            if (_waiting.gameObject.activeSelf != show)
                _waiting.gameObject.SetActive(show);

            if (!show)
                return;

            var ready = match.PlayerCount >= match.MinPlayers;
            var key = match.PlayerCount * 1000 + match.MinPlayers * 2 + (ready ? 1 : 0);
            if (key == _lastWaitingKey)
                return;

            _lastWaitingKey = key;
            _waiting.text = ready && !match.AutoStart
                ? "Pulsa Iniciar partida o Enter"
                : $"Esperando jugadores ({match.PlayerCount}/{match.MinPlayers})";
        }

        private void UpdateEndScreen(MatchManager match, MatchPhase phase)
        {
            if (match == null || phase != MatchPhase.Ended)
            {
                if (_endShown)
                    HideEndScreen();

                return;
            }

            if (!_endShown)
            {
                if (match.Result == MatchResult.None)
                    return;

                ShowEndScreen(match.Result);
            }

            _endElapsed += Time.unscaledDeltaTime;
            _endGroup.alpha = Mathf.Clamp01(_endElapsed / _endFadeDuration);

            var remaining = Mathf.CeilToInt(match.PhaseTimeRemaining);
            if (remaining != _lastRematch)
            {
                _lastRematch = remaining;
                _endRematch.text = $"Revancha en {remaining}…";
            }
        }

        private void ShowEndScreen(MatchResult result)
        {
            _endShown = true;
            _endElapsed = 0f;
            _lastRematch = -1;
            _endGroup.alpha = 0f;
            _endScreen.SetActive(true);

            var camperWon = result == MatchResult.CamperWins;
            var local = PlayerCharacter.Local;
            var isCamper = local == null || local.Role == PlayerRole.Camper;
            var victory = camperWon == isCamper;

            _endTitle.text = camperWon ? "¡Ganó el campista!" : "¡Ganaron los duendes!";
            _endSubtitle.text = victory ? "¡Victoria!" : "Derrota";
            _endSubtitle.color = victory ? _doneColor : _failColor;
            _titlePunch.Pop(0.6f, 1.15f, 0.4f);

            if (camperWon)
            {
                Flash(new Color(1f, 0.6f, 0.2f, 0.3f), 0.6f);
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(6f, 0.6f);
            }
            else
            {
                Flash(new Color(0f, 0f, 0.1f, 0.6f), 0.8f);
            }
        }

        private void HideEndScreen()
        {
            _endShown = false;
            _endScreen.SetActive(false);
        }

        #endregion
    }
}
