using BetterTogether.Audio;
using BetterTogether.Gaze;
using BetterTogether.Player;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// C8.2: everything the camera tells on screen, drawn with uGUI/TMP on the HUD canvas.
    /// Camper: a wooden viewfinder over the real gaze cone (it follows the camera FOV), focus brackets on the locked gnome,
    /// plates, a shot prompt, the shutter flash and the album slot. Gnome: a three-level threat glint at the screen edge
    /// (in a cone, being locked, photographed) and, when frozen, a sepia frame plus a break-free ring around the crosshair
    /// while the view and the rest of the HUD stay intact.
    /// Everything is derived from replicated state in Update/LateUpdate (no RPCs, nothing networked is written); texts are
    /// written only when their value changes. Runs after the Cinemachine brain so the FOV it reads is the one on screen.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class GazeFeedbackHUD : MonoBehaviour
    {
        // The canvas children are grouped under roots; children are cached by index in Awake (order documented per tooltip).
        [Header("References: camper viewfinder (wired by GameHudBuilder)")]
        [Tooltip("Pop root of the viewfinder (scale and active state follow the camera focus amount). Named Viewfinder: the end screen hides it.")]
        [SerializeField] private RectTransform _viewfinderRoot;
        [Tooltip("CanvasGroup on the Content child of the viewfinder root (alpha follows the focus amount; the root keeps its own group free for the end screen).")]
        [SerializeField] private CanvasGroup _viewfinderGroup;
        [Tooltip("4 Image children: top, bottom, left, right darkening outside the cone rectangle.")]
        [SerializeField] private RectTransform _dimRoot;
        [Tooltip("4 children: wooden corners TL, TR, BL, BR (pivot top-left; the HUD mirrors them).")]
        [SerializeField] private RectTransform _cornerRoot;
        [Tooltip("4 Image children: two vertical and two horizontal rule-of-thirds lines.")]
        [SerializeField] private RectTransform _thirdsRoot;
        [Tooltip("Root of the focus brackets; moves onto the locked gnome. 4 children: TL, TR, BL, BR (pivot top-left).")]
        [SerializeField] private RectTransform _bracketRoot;
        [Tooltip("Pill under the brackets with the 'Enfocado' label; shown when the shot is ready.")]
        [SerializeField] private RectTransform _focusedLabel;
        [Tooltip("Plates row. 3 children 'Plate_i' (ghost Image) each with a Filled child 'Fill'. Moves between the viewfinder and the bottom centre.")]
        [SerializeField] private RectTransform _platesRow;
        [Tooltip("Shot prompt pill at the base of the viewfinder.")]
        [SerializeField] private RectTransform _shootPrompt;
        [Tooltip("Label of the shot prompt.")]
        [SerializeField] private TMP_Text _shootText;
        [Tooltip("Photo frame inside the AlbumSlot root (bottom right): its anchored position is the resting place; holds the RawImage that C8.3 fills through ShowPhoto.")]
        [SerializeField] private RectTransform _albumRoot;
        [Tooltip("CanvasGroup on the photo frame.")]
        [SerializeField] private CanvasGroup _albumGroup;
        [Tooltip("RawImage that shows the photo.")]
        [SerializeField] private RawImage _albumImage;

        [Header("References: gnome (wired by GameHudBuilder)")]
        [Tooltip("Lens glint that sits at the screen edge towards the camper (threat levels 1 and 2).")]
        [SerializeField] private Image _threatGlint;
        [Tooltip("Root of the frozen widgets (sepia frame, ring, label); active only while the local gnome is stone.")]
        [SerializeField] private GameObject _frozenRoot;
        [Tooltip("4 Image children: top, bottom, left, right sepia frame, 24 px wide.")]
        [SerializeField] private RectTransform _frozenFrame;
        [Tooltip("Root of the break-free ring around the crosshair (ring + key glyph); it throbs on each key press.")]
        [SerializeField] private RectTransform _ringRoot;
        [Tooltip("Filled radial Image: break-free progress.")]
        [SerializeField] private Image _ringImage;
        [Tooltip("'Sacudete' label under the ring.")]
        [SerializeField] private TMP_Text _shakeLabel;

        [Header("Audio")]
        [Tooltip("Played to the gnome when a camper's lock on it starts (threat level 2). Empty cue = silent.")]
        [SerializeField] private SoundCue _lockedTickSfx;

        [Header("Camper")]
        [Tooltip("Largest half size of the viewfinder rectangle as a fraction of the canvas half size (keeps corners on screen).")]
        [Range(0.5f, 1f)]
        [SerializeField] private float _maxViewfinderFraction = 0.96f;
        [Tooltip("Size in canvas units of one wooden corner.")]
        [SerializeField] private float _cornerSize = 110f;
        [Tooltip("Opacity of the darkening outside the cone, as a fraction of the night80 alpha.")]
        [Range(0f, 1f)]
        [SerializeField] private float _dimStrength = 0.7f;
        [Tooltip("Opacity of the rule-of-thirds grid.")]
        [Range(0f, 1f)]
        [SerializeField] private float _thirdsAlpha = 0.2f;
        [Tooltip("Viewfinder scale at focus amount 0 (it settles to 1 with a short overshoot).")]
        [SerializeField] private float _popScale = 1.15f;
        [Tooltip("Margin of the plates row from the viewfinder corner, canvas units.")]
        [SerializeField] private Vector2 _platesMargin = new(44f, 44f);
        [Tooltip("Scale of the plates row while the camera is down.")]
        [SerializeField] private float _platesRestScale = 0.6f;
        [Tooltip("Distance from the bottom of the screen of the plates row while the camera is down (old stamina bar spot).")]
        [SerializeField] private float _platesRestBottom = 70f;
        [Tooltip("Seconds of the dry-click shake of the plates.")]
        [SerializeField] private float _dryShakeTime = 0.3f;
        [Tooltip("Pixels of the dry-click shake.")]
        [SerializeField] private float _dryShakePixels = 8f;

        [Header("Focus brackets")]
        [Tooltip("Half height in metres of the box the brackets draw around the gnome focus point.")]
        [SerializeField] private float _bracketWorldHalf = 0.8f;
        [Tooltip("Bracket half size limits in canvas units.")]
        [SerializeField] private Vector2 _bracketHalfRange = new(36f, 240f);
        [Tooltip("Bracket size multiplier when the lock starts; it closes to 1 as the lock fills.")]
        [SerializeField] private float _bracketOpenScale = 1.4f;

        [Header("Flash")]
        [Tooltip("Peak alpha of the shutter flash.")]
        [Range(0f, 1f)]
        [SerializeField] private float _flashAlpha = 0.6f;
        [Tooltip("Peak alpha cap with the 'Reducir destellos' option.")]
        [Range(0f, 1f)]
        [SerializeField] private float _reducedFlashAlpha = 0.2f;
        [Tooltip("Seconds of a shutter flash.")]
        [SerializeField] private float _flashDuration = 0.08f;
        [Tooltip("Peak alpha of the soft white flash when a gnome breaks free.")]
        [Range(0f, 1f)]
        [SerializeField] private float _releaseFlashAlpha = 0.25f;

        [Header("Gnome threat")]
        [Tooltip("Glint size at level 1 (in a cone) in canvas units.")]
        [SerializeField] private float _glintMinSize = 52f;
        [Tooltip("Glint size at the end of the lock (level 2).")]
        [SerializeField] private float _glintMaxSize = 150f;
        [Tooltip("Opacity of the glint at level 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float _glintBaseAlpha = 0.5f;
        [Tooltip("Distance of the glint from the screen edge, canvas units.")]
        [SerializeField] private float _glintEdgeInset = 36f;
        [Tooltip("Relative size throb of the glint at level 2; it pulses once per second.")]
        [SerializeField] private float _glintPulse = 0.12f;

        [Header("Break free")]
        [Tooltip("Sideways shake of the label in canvas units on each key press.")]
        [SerializeField] private float _shakePixels = 6f;
        [SerializeField] private float _shakeDuration = 0.06f;
        [Tooltip("Ring scale at the start of its throb on a key press.")]
        [SerializeField] private float _ringPulseScale = 1.1f;
        [Tooltip("Seconds of the ring throb.")]
        [SerializeField] private float _ringPulseTime = 0.08f;

        [Header("Album")]
        [Tooltip("Seconds of the photo drop animation.")]
        [SerializeField] private float _albumDropTime = 0.6f;
        [Tooltip("Seconds the photo stays after it dropped.")]
        [SerializeField] private float _albumHoldTime = 3f;
        [Tooltip("Seconds of the fade at the end.")]
        [SerializeField] private float _albumFadeTime = 0.3f;

        public static GazeFeedbackHUD Instance { get; private set; }

        private const float PulseHz = 1f;

        private RectTransform _canvasRect;
        private Camera _camera;
        private UIPunch _labelPunch;
        private bool _valid;

        private Image[] _dim;
        private RectTransform[] _corners;
        private Image[] _thirds;
        private RectTransform[] _brackets;
        private Image[] _bracketImages;
        private Image _focusedBackground;
        private Image[] _plateFills;
        private Image[] _frameEdges;

        private float _lastHalfW = -1f;
        private float _lastHalfH = -1f;
        private float[] _lastPlateFill;
        private string[] _shootTexts;
        private int _shootTextsMax = -1;
        private int _lastShootIndex = -1;
        private string _keyText;

        // Camper feedback is detected by comparing replicated counters; initialised on the first frame of each gaze.
        private CamperGaze _trackedGaze;
        private byte _lastShotSeq;
        private byte _lastDrySeq;
        private float _dryAge = float.MaxValue;

        // Gnome feedback.
        private Petrifiable _trackedGnome;
        private bool _lastStone;
        private int _lastPresses;
        private int _lastReleases;
        private int _lastThreatLevel;
        private float _lastRingFill = -1f;
        private Tween _ringPulse;

        // Album.
        private Tween _albumTween;
        private bool _albumActive;
        private float _albumStart;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _viewfinderRoot, nameof(_viewfinderRoot))
                     & HudSupport.Require(this, _viewfinderGroup, nameof(_viewfinderGroup))
                     & HudSupport.Require(this, _dimRoot, nameof(_dimRoot))
                     & HudSupport.Require(this, _cornerRoot, nameof(_cornerRoot))
                     & HudSupport.Require(this, _thirdsRoot, nameof(_thirdsRoot))
                     & HudSupport.Require(this, _bracketRoot, nameof(_bracketRoot))
                     & HudSupport.Require(this, _focusedLabel, nameof(_focusedLabel))
                     & HudSupport.Require(this, _platesRow, nameof(_platesRow))
                     & HudSupport.Require(this, _shootPrompt, nameof(_shootPrompt))
                     & HudSupport.Require(this, _shootText, nameof(_shootText))
                     & HudSupport.Require(this, _albumRoot, nameof(_albumRoot))
                     & HudSupport.Require(this, _albumGroup, nameof(_albumGroup))
                     & HudSupport.Require(this, _albumImage, nameof(_albumImage))
                     & HudSupport.Require(this, _threatGlint, nameof(_threatGlint))
                     & HudSupport.Require(this, _frozenRoot, nameof(_frozenRoot))
                     & HudSupport.Require(this, _frozenFrame, nameof(_frozenFrame))
                     & HudSupport.Require(this, _ringRoot, nameof(_ringRoot))
                     & HudSupport.Require(this, _ringImage, nameof(_ringImage))
                     & HudSupport.Require(this, _shakeLabel, nameof(_shakeLabel));
            if (!_valid)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (!HudSupport.Require(this, canvas, "Canvas (parent)"))
            {
                _valid = false;
                return;
            }

            if (_dimRoot.childCount < 4 || _cornerRoot.childCount < 4 || _thirdsRoot.childCount < 4
                || _bracketRoot.childCount < 4 || _frozenFrame.childCount < 4 || _platesRow.childCount < 1)
            {
                Debug.LogError($"[{nameof(GazeFeedbackHUD)}] A viewfinder group on '{name}' has too few children. " +
                               "Run the menu GnoMore/Construir HUD de juego.", this);
                _valid = false;
                return;
            }

            _canvasRect = (RectTransform)canvas.rootCanvas.transform;
            _labelPunch = UIPunch.Of(_shakeLabel);

            CacheChildren();
            ApplyTheme();

            _viewfinderRoot.gameObject.SetActive(false);
            _platesRow.gameObject.SetActive(false);
            _albumRoot.gameObject.SetActive(false);
            _threatGlint.enabled = false;
            _frozenRoot.SetActive(false);
            _bracketRoot.gameObject.SetActive(false);
            _focusedLabel.gameObject.SetActive(false);
            Instance = this;
        }

        private void OnEnable() => PhotoAlbum.PhotoTaken += ShowPhoto;

        private void OnDisable() => PhotoAlbum.PhotoTaken -= ShowPhoto;

        private void OnDestroy()
        {
            _ringPulse.Stop();
            _albumTween.Stop();
            if (Instance == this)
                Instance = null;
        }

        // Children are cached once so per-frame code never calls GetComponent or GetChild.
        private void CacheChildren()
        {
            _dim = new Image[4];
            _thirds = new Image[4];
            _frameEdges = new Image[4];
            _corners = new RectTransform[4];
            _brackets = new RectTransform[4];
            _bracketImages = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                _dim[i] = _dimRoot.GetChild(i).GetComponent<Image>();
                _thirds[i] = _thirdsRoot.GetChild(i).GetComponent<Image>();
                _frameEdges[i] = _frozenFrame.GetChild(i).GetComponent<Image>();
                _corners[i] = (RectTransform)_cornerRoot.GetChild(i);
                _brackets[i] = (RectTransform)_bracketRoot.GetChild(i);
                _bracketImages[i] = _brackets[i].GetComponent<Image>();
            }

            _focusedBackground = _focusedLabel.GetComponent<Image>();

            var plates = _platesRow.childCount;
            _plateFills = new Image[plates];
            _lastPlateFill = new float[plates];
            for (var i = 0; i < plates; i++)
            {
                var fill = _platesRow.GetChild(i).Find("Fill");
                _plateFills[i] = fill != null ? fill.GetComponent<Image>() : null;
                _lastPlateFill[i] = -1f;
            }
        }

        // Colours come from the theme at runtime, never from the prefab.
        private void ApplyTheme()
        {
            var theme = UiTheme.Safe;
            var dim = theme.night80;
            dim.a *= _dimStrength;
            for (var i = 0; i < 4; i++)
            {
                SetGraphic(_dim[i], dim);
                SetGraphic(_thirds[i], UiTheme.WithAlpha(theme.cream, _thirdsAlpha));
                SetGraphic(_frameEdges[i], theme.photoSepia);
            }

            SetGraphic(_focusedBackground, theme.ember);
            var label = _focusedLabel.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.color = theme.ink;

            _shootText.color = theme.cream;
            _shakeLabel.color = theme.cream;
            _ringImage.color = theme.ember;
        }

        private static void SetGraphic(Graphic graphic, Color color)
        {
            if (graphic != null)
            {
                graphic.color = color;
                graphic.raycastTarget = false;
            }
        }

        /// <summary>
        /// Shows a photo in the album slot (bottom right): it drops in with a twist (<c>Album Drop Time</c>), stays a few
        /// seconds and fades. Called by the photo album (C8.3) with a texture it owns.
        /// </summary>
        public void ShowPhoto(Texture tex)
        {
            if (!_valid || tex == null)
                return;

            _albumImage.texture = tex;
            _albumRoot.gameObject.SetActive(true);
            _albumActive = true;
            _albumStart = Time.unscaledTime;
            _albumGroup.alpha = 0f;
            _albumTween.Stop();
            _albumTween = Tween.Custom(this, 0f, 1f, Mathf.Max(0.05f, _albumDropTime),
                static (hud, t) => hud.ApplyAlbumDrop(t), Ease.OutBack, useUnscaledTime: true);
        }

        private void ApplyAlbumDrop(float t)
        {
            // t overshoots 1 with OutBack: unclamped lerps give the bounce.
            _albumRoot.localScale = Vector3.one * Mathf.LerpUnclamped(1.5f, 1f, t);
            _albumRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(18f, -2f, t));
            var offset = Mathf.LerpUnclamped(260f, 0f, t);
            _albumRoot.anchoredPosition = _albumRest + new Vector2(0f, offset);
        }

        private Vector2 _albumRest;
        private bool _albumRestRead;

        // ------------------------------------------------------------------ Update: events

        private void Update()
        {
            if (!_valid)
                return;

            TrackCamperEvents(CamperGaze.Local);
            TrackGnomeEvents(Petrifiable.Local);
            UpdateAlbum();
        }

        // Shots and dry clicks are detected from the replicated sequences. The first frame of each gaze only records them,
        // so a late joiner or a master change never replays an old flash.
        private void TrackCamperEvents(CamperGaze gaze)
        {
            if (gaze != _trackedGaze)
            {
                _trackedGaze = gaze;
                if (gaze != null)
                {
                    _lastShotSeq = gaze.ShotSeq;
                    _lastDrySeq = gaze.DrySeq;
                }

                return;
            }

            if (gaze == null)
                return;

            _dryAge += Time.unscaledDeltaTime;
            var shot = gaze.ShotSeq;
            if (shot != _lastShotSeq)
            {
                _lastShotSeq = shot;
                FlashScreen(_flashAlpha, _flashDuration);
            }

            var dry = gaze.DrySeq;
            if (dry != _lastDrySeq)
            {
                _lastDrySeq = dry;
                _dryAge = 0f;
            }
        }

        private void TrackGnomeEvents(Petrifiable gnome)
        {
            if (gnome != _trackedGnome)
            {
                _trackedGnome = gnome;
                _lastThreatLevel = 0;
                if (gnome != null)
                {
                    _lastStone = gnome.IsStone;
                    _lastPresses = gnome.PressCount;
                    _lastReleases = gnome.ReleaseCount;
                }

                return;
            }

            if (gnome == null)
                return;

            // Level 3: the shot that freezes this gnome.
            var stone = gnome.IsStone;
            if (stone && !_lastStone)
                FlashScreen(_flashAlpha, _flashDuration);
            _lastStone = stone;

            if (gnome.PressCount != _lastPresses)
            {
                _lastPresses = gnome.PressCount;
                _labelPunch.Shake(_shakePixels, _shakeDuration);
                PulseRing();
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(0.6f, 0.08f);
            }

            if (gnome.ReleaseCount == _lastReleases)
                return;

            _lastReleases = gnome.ReleaseCount;
            FlashScreen(_releaseFlashAlpha, 0.15f);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(4f, 0.2f);
        }

        // One flash path for every full-screen flash: "Reducir destellos" caps its peak.
        private void FlashScreen(float peak, float duration)
        {
            var flash = ScreenFlash.Instance;
            if (flash == null)
                return;

            if (FlashSettings.ReduceFlashes)
                peak = Mathf.Min(peak, _reducedFlashAlpha);

            flash.Flash(UiTheme.WithAlpha(UiTheme.Safe.flash, peak), duration);
        }

        private void PulseRing()
        {
            _ringPulse.Stop();
            _ringPulse = Tween.Custom(this, _ringPulseScale, 1f, Mathf.Max(0.01f, _ringPulseTime),
                static (hud, value) => hud._ringRoot.localScale = new Vector3(value, value, 1f), Ease.OutQuad, useUnscaledTime: true);
        }

        private void UpdateAlbum()
        {
            if (!_albumActive)
                return;

            var age = Time.unscaledTime - _albumStart;
            var end = _albumDropTime + _albumHoldTime + _albumFadeTime;
            if (age >= end)
            {
                _albumActive = false;
                _albumTween.Stop();
                _albumRoot.gameObject.SetActive(false);
                return;
            }

            var fadeIn = Mathf.Clamp01(age / 0.1f);
            var fadeOut = Mathf.Clamp01((end - age) / Mathf.Max(0.01f, _albumFadeTime));
            _albumGroup.alpha = Mathf.Min(fadeIn, fadeOut);
        }

        // ------------------------------------------------------------------ LateUpdate: layout

        private void LateUpdate()
        {
            if (!_valid)
                return;

            if (!_albumRestRead)
            {
                // The slot's anchored position in the prefab is its resting place; the drop starts above it.
                _albumRestRead = true;
                _albumRest = _albumRoot.anchoredPosition;
            }

            if (_camera == null)
                _camera = Camera.main;

            var size = _canvasRect.rect.size;
            UpdateCamperView(CamperGaze.Local, size);
            UpdateGnomeView(Petrifiable.Local, size);
        }

        private void UpdateCamperView(CamperGaze gaze, Vector2 size)
        {
            if (gaze == null || _camera == null)
            {
                HudSupport.SetActive(_viewfinderRoot.gameObject, false);
                HudSupport.SetActive(_platesRow.gameObject, false);
                return;
            }

            var gazeOn = gaze.IsGazeOn;
            HudSupport.SetActive(_platesRow.gameObject, gazeOn);
            var focus = CameraFocus.Instance;
            var amount = gazeOn && focus != null ? focus.Amount : 0f;
            var showViewfinder = amount > 0.001f;
            HudSupport.SetActive(_viewfinderRoot.gameObject, showViewfinder);

            // The cone is round: the frame is the square inscribed in it (tan(halfAngle) / √2 per axis, over tan(fov / 2) of the
            // half screen, with the FOV on screen now), so a gnome anywhere inside the frame can be focused.
            var tanHalfV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var tanHalfH = tanHalfV * _camera.aspect;
            var tanCone = Mathf.Tan(Mathf.Min(gaze.HalfAngle, 85f) * Mathf.Deg2Rad) * 0.70710678f;
            var halfW = Mathf.Min(tanCone / tanHalfH * size.x * 0.5f, size.x * 0.5f * _maxViewfinderFraction);
            var halfH = Mathf.Min(tanCone / tanHalfV * size.y * 0.5f, size.y * 0.5f * _maxViewfinderFraction);

            if (gazeOn)
                UpdatePlates(gaze, size, amount, halfW, halfH);

            if (!showViewfinder)
                return;

            _viewfinderGroup.alpha = amount;
            var pop = Mathf.LerpUnclamped(_popScale, 1f, EaseOutBack(amount));
            _viewfinderRoot.localScale = new Vector3(pop, pop, 1f);

            if (!Mathf.Approximately(halfW, _lastHalfW) || !Mathf.Approximately(halfH, _lastHalfH))
            {
                _lastHalfW = halfW;
                _lastHalfH = halfH;
                LayoutViewfinder(halfW, halfH);
            }

            UpdateShootPrompt(gaze);
            UpdateBrackets(gaze, size, tanHalfV);
        }

        // Dim bars, corners and thirds follow the cone rectangle; only runs when it changes (FOV zoom, cone widening, resize).
        private void LayoutViewfinder(float halfW, float halfH)
        {
            const float Big = 6000f;
            SetRect(_dim[0].rectTransform, new Vector2(0f, halfH), new Vector2(Big, Big));
            SetRect(_dim[1].rectTransform, new Vector2(0f, -halfH), new Vector2(Big, Big));
            SetRect(_dim[2].rectTransform, new Vector2(-halfW, 0f), new Vector2(Big, halfH * 2f));
            SetRect(_dim[3].rectTransform, new Vector2(halfW, 0f), new Vector2(Big, halfH * 2f));

            // Corners: one sprite (top-left L), mirrored through the sign of the scale around its top-left pivot.
            PlaceCorner(_corners[0], -halfW, halfH, 1f, 1f, _cornerSize);
            PlaceCorner(_corners[1], halfW, halfH, -1f, 1f, _cornerSize);
            PlaceCorner(_corners[2], -halfW, -halfH, 1f, -1f, _cornerSize);
            PlaceCorner(_corners[3], halfW, -halfH, -1f, -1f, _cornerSize);

            const float Line = 2f;
            SetRect(_thirds[0].rectTransform, new Vector2(-halfW / 3f, 0f), new Vector2(Line, halfH * 2f));
            SetRect(_thirds[1].rectTransform, new Vector2(halfW / 3f, 0f), new Vector2(Line, halfH * 2f));
            SetRect(_thirds[2].rectTransform, new Vector2(0f, -halfH / 3f), new Vector2(halfW * 2f, Line));
            SetRect(_thirds[3].rectTransform, new Vector2(0f, halfH / 3f), new Vector2(halfW * 2f, Line));

            _shootPrompt.anchoredPosition = new Vector2(0f, -halfH + 24f);
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void PlaceCorner(RectTransform rect, float x, float y, float signX, float signY, float size)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, size);
            rect.localScale = new Vector3(signX, signY, 1f);
        }

        // 3 plates: full when loaded, 30 % when empty (the ghost Image), the one recharging fills with NextPlateFraction.
        private void UpdatePlates(CamperGaze gaze, Vector2 size, float amount, float halfW, float halfH)
        {
            var plates = gaze.Plates;
            var whole = Mathf.FloorToInt(plates + 0.0001f);
            var next = gaze.NextPlateFraction;
            var max = gaze.MaxPlates;
            for (var i = 0; i < _plateFills.Length; i++)
            {
                var plate = _platesRow.GetChild(i).gameObject;
                HudSupport.SetActive(plate, i < max);
                var fill = i < whole ? 1f : i == whole ? next : 0f;
                if (_plateFills[i] == null || Mathf.Approximately(fill, _lastPlateFill[i]))
                    continue;

                _lastPlateFill[i] = fill;
                _plateFills[i].fillAmount = fill;
            }

            // Between the viewfinder (top-left, inside the corner) and the old stamina-bar spot (bottom centre, small).
            var rowSize = _platesRow.sizeDelta;
            var viewfinderPos = new Vector2(-halfW + _platesMargin.x, halfH - _platesMargin.y);
            var restPos = new Vector2(-rowSize.x * _platesRestScale * 0.5f, -size.y * 0.5f + _platesRestBottom + rowSize.y * _platesRestScale);
            var position = Vector2.Lerp(restPos, viewfinderPos, amount);
            if (_dryAge < _dryShakeTime)
            {
                var decay = 1f - _dryAge / _dryShakeTime;
                position.x += Mathf.Sin(_dryAge * 70f) * _dryShakePixels * decay;
            }

            _platesRow.anchoredPosition = position;
            var scale = Mathf.Lerp(_platesRestScale, 1f, amount);
            _platesRow.localScale = new Vector3(scale, scale, 1f);
        }

        // "Clic · Disparar · N placas", or "Sin placas"; strings are built once per plate count, text written on change only.
        private void UpdateShootPrompt(CamperGaze gaze)
        {
            var max = gaze.MaxPlates;
            if (_shootTexts == null || _shootTextsMax != max)
            {
                _shootTextsMax = max;
                _keyText ??= ControlLabels.FireKeyText();
                _shootTexts = new string[max + 1];
                _shootTexts[0] = "Sin placas";
                for (var n = 1; n <= max; n++)
                    _shootTexts[n] = $"{_keyText} · Disparar  ·  {n} {(n == 1 ? "placa" : "placas")}";
                _lastShootIndex = -1;
            }

            var count = Mathf.Clamp(Mathf.FloorToInt(gaze.Plates + 0.0001f), 0, max);
            if (count == _lastShootIndex)
                return;

            _lastShootIndex = count;
            _shootText.text = _shootTexts[count];
            var theme = UiTheme.Safe;
            var cream = theme.cream;
            if (count == 0)
                cream.a = 0.6f; // Dimmed but readable on the dark pill.
            _shootText.color = cream;
        }

        // Brackets close from _bracketOpenScale to 1 with the lock; cream -> ember once the shot is ready.
        private void UpdateBrackets(CamperGaze gaze, Vector2 size, float tanHalfV)
        {
            var target = gaze.LockedTarget;
            if (target == null || target.IsStone)
            {
                HudSupport.SetActive(_bracketRoot.gameObject, false);
                return;
            }

            var screen = _camera.WorldToScreenPoint(target.FocusPoint);
            if (screen.z <= 0.05f)
            {
                HudSupport.SetActive(_bracketRoot.gameObject, false);
                return;
            }

            HudSupport.SetActive(_bracketRoot.gameObject, true);
            var toCanvas = size.x / Mathf.Max(1f, _camera.pixelWidth);
            _bracketRoot.anchoredPosition = new Vector2(
                (screen.x - _camera.pixelWidth * 0.5f) * toCanvas,
                (screen.y - _camera.pixelHeight * 0.5f) * toCanvas);

            // Canvas units per metre at that distance, from the vertical FOV.
            var perMetre = size.y * 0.5f / (screen.z * tanHalfV);
            var half = Mathf.Clamp(_bracketWorldHalf * perMetre, _bracketHalfRange.x, _bracketHalfRange.y);
            half *= Mathf.Lerp(_bracketOpenScale, 1f, gaze.LockProgress);

            _brackets[0].anchoredPosition = new Vector2(-half, half);
            _brackets[1].anchoredPosition = new Vector2(half, half);
            _brackets[2].anchoredPosition = new Vector2(-half, -half);
            _brackets[3].anchoredPosition = new Vector2(half, -half);

            var theme = UiTheme.Safe;
            var ready = gaze.FocusReady;
            var color = ready ? theme.ember : theme.cream;
            for (var i = 0; i < 4; i++)
                _bracketImages[i].color = color;

            HudSupport.SetActive(_focusedLabel.gameObject, ready);
            if (ready)
                _focusedLabel.anchoredPosition = new Vector2(0f, -half - 14f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var u = Mathf.Clamp01(t) - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ------------------------------------------------------------------ Gnome view

        private void UpdateGnomeView(Petrifiable gnome, Vector2 size)
        {
            if (gnome == null)
            {
                HudSupport.SetEnabled(_threatGlint, false);
                HudSupport.SetActive(_frozenRoot, false);
                return;
            }

            if (gnome.IsStone)
            {
                _lastThreatLevel = 3;
                HudSupport.SetEnabled(_threatGlint, false);
                HudSupport.SetActive(_frozenRoot, true);
                var fill = gnome.BreakFraction;
                if (!Mathf.Approximately(fill, _lastRingFill))
                {
                    _lastRingFill = fill;
                    _ringImage.fillAmount = fill;
                }

                return;
            }

            HudSupport.SetActive(_frozenRoot, false);
            _lastRingFill = -1f;
            UpdateThreat(gnome, size);
        }

        // Level 1: inside a cone. Level 2: locked (the glint grows and turns ember, pulsing once per second). Level 3 is the
        // frozen state above, with the flash fired from the Update tracker.
        private void UpdateThreat(Petrifiable gnome, Vector2 size)
        {
            if (_camera == null || gnome.WarningProgress <= 0.001f)
            {
                SetThreatLevel(0);
                return;
            }

            CamperGaze locker = null;
            CamperGaze nearest = null;
            var nearestSqr = float.MaxValue;
            var gnomePos = gnome.transform.position;
            var all = CamperGaze.All;
            for (var i = 0; i < all.Count; i++)
            {
                var gaze = all[i];
                if (gaze == null || !gaze.IsActive)
                    continue;

                if (gaze.LockedTarget == gnome)
                    locker = gaze;
                else if (!gaze.CanSee(gnome.FocusPoint, gnome.transform))
                    continue; // Only a camera that actually frames this gnome can be the warning's source.

                var sqr = (gaze.EyePosition - gnomePos).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = gaze;
                }
            }

            var source = locker != null ? locker : nearest;
            if (source == null)
            {
                SetThreatLevel(0);
                return;
            }

            var progress = locker != null ? Mathf.Clamp01(locker.LockProgress) : 0f;
            var level = progress > 0f ? 2 : 1;
            SetThreatLevel(level);

            var theme = UiTheme.Safe;
            var glintSize = _glintMinSize;
            var color = UiTheme.WithAlpha(theme.cream, _glintBaseAlpha);
            if (level == 2)
            {
                glintSize = Mathf.Lerp(_glintMinSize, _glintMaxSize, progress);
                glintSize *= 1f + _glintPulse * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * PulseHz);
                color = Color.Lerp(color, theme.ember, progress);
            }

            _threatGlint.color = color;
            var rect = _threatGlint.rectTransform;
            rect.sizeDelta = new Vector2(glintSize, glintSize);
            rect.anchoredPosition = EdgePosition(source.EyePosition, size, glintSize * 0.5f + _glintEdgeInset);
        }

        private void SetThreatLevel(int level)
        {
            HudSupport.SetEnabled(_threatGlint, level > 0);
            if (level == 2 && _lastThreatLevel < 2 && _lockedTickSfx != null)
                _lockedTickSfx.Play();

            _lastThreatLevel = level;
        }

        // Where the ray from the screen centre towards a world point leaves the screen rectangle (inset); a point behind the camera
        // is mirrored so the glint points the way to turn.
        private Vector2 EdgePosition(Vector3 world, Vector2 size, float inset)
        {
            var screen = _camera.WorldToScreenPoint(world);
            var toCanvas = size.x / Mathf.Max(1f, _camera.pixelWidth);
            var dx = (screen.x - _camera.pixelWidth * 0.5f) * toCanvas;
            var dy = (screen.y - _camera.pixelHeight * 0.5f) * toCanvas;
            if (screen.z < 0f)
            {
                dx = -dx;
                dy = -dy;
            }

            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f)
                dy = -1f;

            var limitX = Mathf.Max(1f, size.x * 0.5f - inset);
            var limitY = Mathf.Max(1f, size.y * 0.5f - inset);
            var scale = Mathf.Min(limitX / Mathf.Max(Mathf.Abs(dx), 0.0001f), limitY / Mathf.Max(Mathf.Abs(dy), 0.0001f));
            return new Vector2(dx * scale, dy * scale);
        }
    }
}
