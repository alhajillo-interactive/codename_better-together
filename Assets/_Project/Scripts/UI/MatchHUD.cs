using System.Collections.Generic;
using BetterTogether.Environment;
using BetterTogether.Fireplace;
using BetterTogether.Interaction;
using BetterTogether.Match;
using BetterTogether.Player;
using PrimeTween;
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
            // Optional child "Stamp" of the line text (made by UiThemeApplier): ink check when done, cross when it failed.
            public Image Stamp;
            public RectTransform StampRect;
            public int StampKind; // 0 hidden, 1 check, 2 cross
            public Tween StampTween;
        }

        [Header("Clock")]
        [SerializeField] private RectTransform _clock;
        [SerializeField] private RectTransform _track;
        [SerializeField] private RectTransform _sun;
        [SerializeField] private Image _sunImage;
        [SerializeField] private TMP_Text _timeLabel;
        [SerializeField] private Color _sunColor = new(1f, 0.85f, 0.3f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text
        [SerializeField] private Color _moonColor = new(0.7f, 0.8f, 1f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text
        [SerializeField] private Color _urgentColor = new(1f, 0.25f, 0.2f); // ui-color-ok: serialized default for a world/scene-space graphic (tunable in the prefab), not themed text
        [SerializeField] private float _urgentSeconds = 10f;

        [Header("Objectives")]
        [SerializeField] private TMP_Text _objectivesTitle;
        [SerializeField] private TMP_Text[] _objectiveTexts;
        [Tooltip("Seconds a line shows the failure colour and the cross stamp after it stops being done.")]
        [SerializeField] private float _failSeconds = 0.9f;
        [Tooltip("Scale the stamp starts at when it drops onto a line (it settles at 1).")]
        [SerializeField] private float _stampDropScale = 1.4f;
        [Tooltip("Seconds the stamp takes to drop.")]
        [SerializeField] private float _stampDropSeconds = 0.18f;

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
        [Tooltip("Optional text for the gnome ranking, MVP and match numbers. When empty, one is cloned from the subtitle at start.")]
        [SerializeField] private TMP_Text _endScores;
        [Tooltip("Font size of the cloned ranking text (only used when End Scores is empty).")]
        [SerializeField] private float _endScoresFontSize = 26f;
        [Tooltip("Pixels between the rematch line and the cloned ranking text below it (only used when End Scores is empty).")]
        [SerializeField] private float _endScoresGap = 12f;

        private readonly List<MatchManager.ScoreEntry> _scoreRows = new();
        private readonly System.Text.StringBuilder _scoreText = new();

        private static readonly Dictionary<string, string> StruckCache = new();

        private readonly Queue<Announcement> _queue = new();
        private Line[] _lines;
        private DayNightCycle _dayCycle;
        private float _nextDaySearch;
        private UIPunch _clockPunch;
        private UIPunch _announcerPunch;
        private UIPunch _titlePunch;
        private ElasticPlate _titlePlate;

        // Toast pill (optional parent "AnnouncerPill" of the announcer text, made by UiThemeApplier).
        private Image _pill;
        private CanvasGroup _pillGroup;
        private ElasticPlate _pillPlate;

        // Parts of the match HUD hidden by the end screen (found by name, hidden with a CanvasGroup so their logic keeps running).
        private static readonly string[] HiddenOnEnd = { "Objectives", "Clock", "ControlsHint", "Prompt", "StaminaBar", "StoneOverlay" };
        private CanvasGroup[] _endHidden;
        private float[] _endHiddenAlpha;

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
        private bool _prevComeback;
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

        /// <summary>The HUD of this client, if one is in the scene.</summary>
        public static MatchHUD Instance { get; private set; }

        /// <summary>True while the end screen is up (other HUD parts must not show themselves then).</summary>
        public bool EndScreenShown => _endShown;

        /// <summary>Shows a message in the announcer of this client only. Safe to call without a HUD.</summary>
        public static void AnnounceLocal(string text, Color color)
        {
            if (Instance != null)
                Instance.Announce(text, color);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            _clockPunch = UIPunch.Of(_clock);
            _titlePunch = UIPunch.Of(_endTitle);
            _titlePlate = _objectivesTitle != null ? _objectivesTitle.GetComponentInParent<ElasticPlate>(true) : null;

            _lines = new Line[ObjectiveCount];
            for (var i = 0; i < ObjectiveCount; i++)
            {
                var line = new Line { Text = _objectiveTexts[i], Punch = UIPunch.Of(_objectiveTexts[i]) };
                var stamp = _objectiveTexts[i].transform.Find("Stamp");
                if (stamp != null && stamp.TryGetComponent(out line.Stamp))
                {
                    line.StampRect = (RectTransform)stamp;
                    line.Stamp.raycastTarget = false;
                    line.Stamp.enabled = false;
                }

                _lines[i] = line;
            }

            var pillParent = _announcer.transform.parent;
            if (pillParent != null && pillParent.name == "AnnouncerPill" && pillParent.TryGetComponent(out _pill))
            {
                _pillGroup = pillParent.GetComponent<CanvasGroup>();
                _pillPlate = pillParent.GetComponent<ElasticPlate>();
                _announcerPunch = UIPunch.Of(pillParent);
                _announcer.color = Theme.cream;
            }
            else
            {
                _announcerPunch = UIPunch.Of(_announcer);
            }

            SetAnnouncerAlpha(0f);
            _waiting.gameObject.SetActive(false);
            _endScreen.SetActive(false);

            if (_endScores == null && _endSubtitle != null)
            {
                _endScores = Instantiate(_endSubtitle, _endSubtitle.transform.parent);
                _endScores.name = "EndScores";
                _endScores.fontSize = _endScoresFontSize;
                _endScores.enableAutoSizing = false;
                _endScores.alignment = TextAlignmentOptions.Top;
                _endScores.color = Theme.ink; // sits on the paper end card
                _endScores.richText = true;
                // Hangs from the bottom edge of the rematch line, derived from its own rect.
                var rematch = _endRematch.rectTransform;
                var rect = _endScores.rectTransform;
                rect.anchorMin = rematch.anchorMin;
                rect.anchorMax = rematch.anchorMax;
                rect.sizeDelta = rematch.sizeDelta;
                rect.pivot = new Vector2(rematch.pivot.x, 1f);
                rect.anchoredPosition = rematch.anchoredPosition
                    - new Vector2(0f, rematch.rect.height * rematch.pivot.y + _endScoresGap);
            }
        }

        private static UiTheme Theme => UiTheme.Safe;

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
            _timeLabel.color = urgent ? _urgentColor : night ? _moonColor : Theme.cream;

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

            // A new variant is a new set of lines, not a state change of the old ones: no juice, no fail flash.
            foreach (var line in _lines)
                line.Known = false;

            _objectivesTitle.SetText(variant switch
            {
                0 => "Hito 1 · Arma la fogata",
                1 => "Hito 2 · Defiéndela",
                2 => "Hito 1 · Sabotea al campista",
                _ => "Hito 2 · ¡Ataca la fogata!",
            });

            // The plate grows with the title (and shrinks the font when it hits its maximum width).
            if (_titlePlate != null)
                _titlePlate.Refresh();
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
            var changed = line.Known && done != line.Done && playing;

            if (changed)
            {
                if (done)
                {
                    line.Punch.Punch(1.25f, 0.25f);
                }
                else
                {
                    line.Punch.Shake(10f, 0.4f);
                    line.RedUntil = Time.unscaledTime + _failSeconds;
                }
            }

            line.Known = true;
            line.Done = done;

            // The lines sit on paper: pending in ink, finished in the soft ink (plus strikethrough and a stamp), failed in hat red.
            var failing = Time.unscaledTime < line.RedUntil;
            var theme = Theme;
            var color = failing ? theme.hatDark : muted || done ? theme.inkSoft : theme.ink;
            if (line.Text.color != color)
                line.Text.color = color;

            SetStamp(line, failing ? 2 : done && !muted ? 1 : 0, changed);

            var fullKey = key * 2 + (done ? 1 : 0);
            if (fullKey == line.Key)
                return false;

            line.Key = fullKey;
            return true;
        }

        // Shows the check or cross stamp of a line (0 hides it). It drops in (scale 1.4 to 1) only on a live change.
        private void SetStamp(Line line, int kind, bool animate)
        {
            if (line.Stamp == null || kind == line.StampKind)
                return;

            line.StampKind = kind;
            line.StampTween.Stop();
            var theme = Theme;
            var sprite = kind == 2 ? theme.stampFail : theme.stamp;
            if (kind == 0 || sprite == null)
            {
                line.Stamp.enabled = false;
                return;
            }

            line.Stamp.sprite = sprite;
            line.Stamp.enabled = true;
            line.StampRect.localScale = Vector3.one;
            if (animate)
            {
                line.StampTween = Tween.Scale(line.StampRect, _stampDropScale, 1f, _stampDropSeconds, Ease.OutQuad,
                    useUnscaledTime: true);
            }
        }

        #endregion

        #region Announcements

        private void DetectPhase(MatchManager match, MatchPhase phase)
        {
            if (phase != _lastPhase)
            {
                if (_lastPhase == MatchPhase.Countdown && phase == MatchPhase.Playing)
                    Announce("¡A jugar!", Theme.leafDark, true);

                _lastPhase = phase;
                _lastCountdown = -1;
            }

            if (match == null || phase != MatchPhase.Countdown)
                return;

            var remaining = Mathf.CeilToInt(match.PhaseTimeRemaining);
            if (remaining == _lastCountdown || remaining <= 0)
                return;

            _lastCountdown = remaining;
            Announce(remaining.ToString(), Theme.ember, true, 0.7f);
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

            // previous is -1 on the first frame seen, so a late joiner never gets past warnings.
            if (!playing)
                return;

            if (previous > 30f && now <= 30f)
                Announce("Se acerca la noche…", Theme.roleGnome);
            else if (previous > 10f && now <= 10f)
                Announce("¡10 segundos para la noche!", Theme.hatDark);
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
                Announce($"¡{Mathf.CeilToInt(_urgentSeconds)} segundos!", Theme.hatDark);
                Flash(new Color(0.2f, 0.3f, 0.8f, 0.15f), 0.4f); // ui-color-ok: full-screen flash, not text
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

            // No local player (yet) means no role: nothing is announced, and the state is still tracked.
            var hasRole = PlayerCharacter.Local != null;
            var camper = hasRole && PlayerCharacter.Local.Role == PlayerRole.Camper;
            var lit = fire.IsLit;
            if (_hasFireState && playing && hasRole && lit != _prevLit)
            {
                if (lit)
                {
                    Announce(camper ? "¡Fuego! Defiéndelo mientras se cocina" : "¡FUEGO! Ahora los duendes atacan",
                        Theme.ember, true, 1.6f);
                    Flash(new Color(1f, 0.6f, 0.2f, 0.2f), 0.4f); // ui-color-ok: full-screen flash, not text
                    if (CameraPunch.Instance != null)
                        CameraPunch.Instance.PunchFov(5f, 0.4f);
                }
                else
                {
                    Announce(camper ? "¡Apagada! Vuelve a encenderla" : "¡Apagada! Que no la vuelva a prender", Theme.roleGnome, true, 1.6f);
                    Flash(new Color(0.3f, 0.5f, 1f, 0.25f), 0.4f); // ui-color-ok: full-screen flash, not text
                }
            }

            // Warn the camper when the water starts to pour (not again right away if a pour is interrupted and restarted).
            var doused = fire.IsBeingDoused;
            if (_hasFireState && playing && doused && !_prevDoused && hasRole && Time.unscaledTime >= _nextDouseWarning && camper)
            {
                Announce("¡Te apagan la fogata!", Theme.hatDark);
                _nextDouseWarning = Time.unscaledTime + 5f;
            }

            _prevDoused = doused;
            // Derived from the replicated comeback timer, so a late joiner (first frame seen) gets no announcement.
            var comeback = fire.ComebackActive;
            if (_hasFireState && playing && hasRole && comeback && !_prevComeback)
            {
                Announce(camper ? "¡Segundo aire! Los fósforos vuelven y tu mirada se amplía" : "¡El campista se recupera!",
                    camper ? Theme.leafDark : Theme.hatDark);
            }

            _prevComeback = comeback;
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
                    Announce("¡La salchicha está lista!", Theme.leafDark);
                else if (state == Sausage.CookState.Burnt)
                    Announce("¡Se quemó la salchicha!", Theme.hatDark);
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
            if (_pill != null)
            {
                // Cream text on the dark pill; the event colour goes to the pill border.
                _pill.color = announcement.Color;
                _pillPlate.Refresh();
            }
            else
            {
                _announcer.color = announcement.Color;
            }

            _announcerPunch.Pop(0.6f, 1.15f, _popDuration);
        }

        private void SetAnnouncerAlpha(float alpha)
        {
            if (_pillGroup != null)
                _pillGroup.alpha = alpha;
            else
                _announcer.alpha = alpha;
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

                SetAnnouncerAlpha(Mathf.Clamp01(alpha));
                if (_currentElapsed >= fadeStart + _fadeDuration)
                {
                    _hasCurrent = false;
                    SetAnnouncerAlpha(0f);
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
            HideMatchHud();

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
            _endSubtitle.color = victory ? Theme.leafDark : Theme.hatDark; // sits on the paper end card
            _titlePunch.Pop(0.6f, 1.15f, 0.4f);
            BuildScoreboard();

            if (camperWon)
            {
                Flash(new Color(1f, 0.6f, 0.2f, 0.3f), 0.6f); // ui-color-ok: full-screen flash, not text
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(6f, 0.6f);
            }
            else
            {
                Flash(new Color(0f, 0f, 0.1f, 0.6f), 0.8f); // ui-color-ok: full-screen flash, not text
            }
        }

        // Once per end screen (also for a late joiner, who reads the replicated scores): ranking, MVP and three match numbers.
        private void BuildScoreboard()
        {
            if (_endScores == null)
                return;

            var match = MatchManager.Instance;
            if (match == null)
            {
                _endScores.text = string.Empty;
                return;
            }

            match.GetScoreRows(_scoreRows);

            // Gnomes still connected without a row (joined late, never scored) appear with 0 points.
            var all = PlayerCharacter.All;
            for (var i = 0; i < all.Count; i++)
            {
                var character = all[i];
                if (character == null || character.Object == null || !character.Object.IsValid || character.Role != PlayerRole.Gnome)
                    continue;

                var owner = character.Object.StateAuthority;
                var found = false;
                for (var j = 0; j < _scoreRows.Count && !found; j++)
                    found = _scoreRows[j].Player == owner;

                if (!found)
                    _scoreRows.Add(new MatchManager.ScoreEntry { Player = owner });
            }

            for (var i = 1; i < _scoreRows.Count; i++)
            {
                var row = _scoreRows[i];
                var j = i - 1;
                while (j >= 0 && _scoreRows[j].Points < row.Points)
                {
                    _scoreRows[j + 1] = _scoreRows[j];
                    j--;
                }

                _scoreRows[j + 1] = row;
            }

            var sb = _scoreText;
            sb.Clear();
            var best = _scoreRows.Count > 0 ? _scoreRows[0].Points : 0;
            for (var i = 0; i < _scoreRows.Count; i++)
                sb.Append(i + 1).Append(". ").Append(NameOf(_scoreRows[i].Player)).Append(" - ").Append(_scoreRows[i].Points).Append(" pts\n");

            if (_scoreRows.Count > 0 && best > 0)
            {
                sb.Append("<b>MVP: "); // bold ink on the paper end card (yellow on paper is unreadable)
                var first = true;
                for (var i = 0; i < _scoreRows.Count && _scoreRows[i].Points == best; i++)
                {
                    if (!first)
                        sb.Append(" y ");
                    sb.Append(NameOf(_scoreRows[i].Player));
                    first = false;
                }

                sb.Append("</b>\n");
            }

            var fire = match.FirstFireSeconds;
            sb.Append("Fuego: ");
            if (fire < 0f)
                sb.Append("sin fuego");
            else
                sb.Append(Mathf.FloorToInt(fire / 60f)).Append(':').Append(Mathf.FloorToInt(fire % 60f).ToString("00"));
            sb.Append("  |  Fogata apagada: ").Append(match.FiresExtinguished)
                .Append(" veces  |  Objetos escondidos: ").Append(match.ObjectsHidden);
            _endScores.text = sb.ToString();
        }

        // The player's name, or "Jugador N" when it has left (its character no longer exists).
        private static string NameOf(Fusion.PlayerRef player)
        {
            var all = PlayerCharacter.All;
            for (var i = 0; i < all.Count; i++)
            {
                var character = all[i];
                if (character != null && character.Object != null && character.Object.IsValid && character.Object.StateAuthority == player)
                    return character.DisplayName;
            }

            return $"Jugador {player.PlayerId}";
        }

        private void HideEndScreen()
        {
            _endShown = false;
            _endScreen.SetActive(false);
            ShowMatchHud();
        }

        // The end screen stands alone: objectives, clock, controls, prompt, energy bar and stone notice go invisible.
        // A CanvasGroup (not SetActive) hides them, since their own scripts keep running; alpha is held at 0 every frame
        // because a script may fade one of them (ControlsHint does), and restored when the end screen goes away.
        private void HideMatchHud()
        {
            if (_endHidden == null)
                CollectHiddenOnEnd();

            for (var i = 0; i < _endHidden.Length; i++)
            {
                var group = _endHidden[i];
                if (group == null || group.alpha == 0f)
                    continue;

                if (_endHiddenAlpha[i] < 0f)
                    _endHiddenAlpha[i] = group.alpha;

                group.alpha = 0f;
            }
        }

        private void ShowMatchHud()
        {
            if (_endHidden == null)
                return;

            for (var i = 0; i < _endHidden.Length; i++)
            {
                if (_endHidden[i] != null && _endHiddenAlpha[i] >= 0f)
                    _endHidden[i].alpha = _endHiddenAlpha[i];

                _endHiddenAlpha[i] = -1f;
            }
        }

        private void CollectHiddenOnEnd()
        {
            _endHidden = new CanvasGroup[HiddenOnEnd.Length];
            _endHiddenAlpha = new float[HiddenOnEnd.Length];
            for (var i = 0; i < HiddenOnEnd.Length; i++)
            {
                _endHiddenAlpha[i] = -1f;
                var part = transform.Find(HiddenOnEnd[i]);
                if (part == null)
                    continue;

                _endHidden[i] = part.TryGetComponent<CanvasGroup>(out var group) ? group : part.gameObject.AddComponent<CanvasGroup>();
            }
        }

        #endregion
    }
}
