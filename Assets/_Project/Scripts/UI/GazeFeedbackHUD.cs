using BetterTogether.Gaze;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Feedback for the petrifying gaze, drawn with uGUI/TMP on the HUD canvas. Gnomes see a vignette that grows while
    /// locked, and a stone screen with a button-mashing counter when petrified; campers see a faint vignette outside
    /// their gaze cone that pulses whenever a gnome is locked, the focus vignette and the stamina bar.
    /// Texts are written only when their value changes; vignettes are Images sized from the canvas.
    /// </summary>
    public class GazeFeedbackHUD : MonoBehaviour
    {
        [Header("References: gnome (wired by GameHudBuilder)")]
        [Tooltip("Full-screen edge vignette: warning tint while a camper looks at the gnome, grey while petrified.")]
        [SerializeField] private Image _gnomeVignette;
        [Tooltip("Root of the petrified overlay (title, counter, bar); active only while the local gnome is stone.")]
        [SerializeField] private GameObject _stoneRoot;
        [Tooltip("Title of the petrified overlay; it shakes on every key press.")]
        [SerializeField] private TMP_Text _stoneTitle;
        [Tooltip("Press counter of the petrified overlay, 'presses/required'.")]
        [SerializeField] private TMP_Text _stoneCounter;
        [Tooltip("Stretched child of the break-free bar background; its right anchor is the progress.")]
        [SerializeField] private RectTransform _stoneBarFill;

        [Header("References: camper (wired by GameHudBuilder)")]
        [Tooltip("Dark vignette that follows the camera focus amount.")]
        [SerializeField] private Image _focusVignette;
        [Tooltip("Faint vignette outside the gaze cone; pulses when a gnome gets locked.")]
        [SerializeField] private Image _coneVignette;
        [Tooltip("Root of the stamina bar; active only while the gaze is on.")]
        [SerializeField] private GameObject _staminaRoot;
        [Tooltip("Stretched child of the stamina bar background; its right anchor is the stamina.")]
        [SerializeField] private RectTransform _staminaFill;
        [Tooltip("Image of the stamina fill, tinted normal/exhausted.")]
        [SerializeField] private Image _staminaFillImage;
        [Tooltip("Tick that marks the stamina fraction needed to use the gaze again.")]
        [SerializeField] private RectTransform _staminaThreshold;
        [Tooltip("'Mirada agotada' label above the bar, shown while exhausted.")]
        [SerializeField] private TMP_Text _exhaustedLabel;
        [Tooltip("'[Clic der.] Mirada' hint next to the bar, shown when the stamina is full and idle.")]
        [SerializeField] private TMP_Text _hintLabel;

        [Header("Gnome")]
        [SerializeField] private Color _warningColor = new(0.45f, 0.55f, 0.7f, 0.8f);
        [Tooltip("Cooler tint for being inside a camper's cone without being locked yet.")]
        [SerializeField] private Color _coneWarningColor = new(0.3f, 0.5f, 0.85f, 0.8f);
        [SerializeField] private Color _petrifiedColor = new(0.5f, 0.5f, 0.52f, 0.9f);
        [Tooltip("Fraction of the half screen that stays clear in the gnome vignette.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _warningInnerRadius = 0.35f;

        [Header("Break free")]
        [Tooltip("Sideways shake of the title in canvas units on each key press.")]
        [SerializeField] private float _shakePixels = 6f;
        [SerializeField] private float _shakeDuration = 0.06f;
        [SerializeField] private Color _releaseFlashColor = new(1f, 1f, 1f, 0.25f);

        [Header("Camper")]
        [SerializeField] private Color _coneColor = new(0.3f, 0.2f, 0.5f, 1f);
        [SerializeField] private float _coneVignetteAlpha = 0.12f;
        [SerializeField] private float _pulseAlpha = 0.35f;
        [SerializeField] private float _pulseDuration = 0.3f;

        [Header("Focus vignette")]
        [Tooltip("Colour of the dark vignette; its alpha is the peak opacity at full focus.")]
        [SerializeField] private Color _focusVignetteColor = new(0f, 0f, 0f, 0.55f);
        [Tooltip("Fraction of the screen centre that stays clear.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _focusVignetteInner = 0.55f;

        [Header("Stamina bar")]
        [SerializeField] private Color _staminaColor = new(0.6f, 0.5f, 1f, 0.95f);
        [SerializeField] private Color _exhaustedColor = new(1f, 0.2f, 0.2f, 0.95f);
        [Tooltip("Blinks per second of the bar while exhausted.")]
        [SerializeField] private float _exhaustedBlinkHz = 3f;

        private RectTransform _canvasRect;
        private Camera _camera;
        private UIPunch _titlePunch;
        private bool _valid;

        private Vector2 _lastCanvasSize;
        private float _lastConeHalfAngle = -1f;
        private float _lastConeFov = -1f;
        private int _lastCounterKey = -1;
        private float _lastBreakFill = -1f;
        private float _lastStaminaFill = -1f;
        private float _lastThreshold = -1f;

        private float _pulseTime = float.MaxValue;
        private Petrifiable _trackedGnome;
        private Petrifiable _lastLocked;
        private int _lastPresses;
        private int _lastReleases;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _gnomeVignette, nameof(_gnomeVignette))
                     & HudSupport.Require(this, _stoneRoot, nameof(_stoneRoot))
                     & HudSupport.Require(this, _stoneTitle, nameof(_stoneTitle))
                     & HudSupport.Require(this, _stoneCounter, nameof(_stoneCounter))
                     & HudSupport.Require(this, _stoneBarFill, nameof(_stoneBarFill))
                     & HudSupport.Require(this, _focusVignette, nameof(_focusVignette))
                     & HudSupport.Require(this, _coneVignette, nameof(_coneVignette))
                     & HudSupport.Require(this, _staminaRoot, nameof(_staminaRoot))
                     & HudSupport.Require(this, _staminaFill, nameof(_staminaFill))
                     & HudSupport.Require(this, _staminaFillImage, nameof(_staminaFillImage))
                     & HudSupport.Require(this, _staminaThreshold, nameof(_staminaThreshold))
                     & HudSupport.Require(this, _exhaustedLabel, nameof(_exhaustedLabel))
                     & HudSupport.Require(this, _hintLabel, nameof(_hintLabel));
            if (!_valid)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (!HudSupport.Require(this, canvas, "Canvas (parent)"))
            {
                _valid = false;
                return;
            }

            _canvasRect = (RectTransform)canvas.rootCanvas.transform;
            _titlePunch = UIPunch.Of(_stoneTitle);

            _gnomeVignette.raycastTarget = false;
            _focusVignette.raycastTarget = false;
            _coneVignette.raycastTarget = false;
            _gnomeVignette.enabled = false;
            _focusVignette.enabled = false;
            _coneVignette.enabled = false;
            _stoneRoot.SetActive(false);
            _staminaRoot.SetActive(false);
            _exhaustedLabel.gameObject.SetActive(false);
            _hintLabel.gameObject.SetActive(false);
        }

        private void Update()
        {
            _pulseTime += Time.deltaTime;
            TrackCamperLock();
            TrackGnomeFeedback();

            if (!_valid)
                return;

            RefreshLayout();
            UpdateGnome(Petrifiable.Local);
            UpdateCamper(CamperGaze.Local);
        }

        private void TrackCamperLock()
        {
            var gaze = CamperGaze.Local;
            var locked = gaze != null ? gaze.LockedTarget : null;
            if (locked != null && locked != _lastLocked)
                _pulseTime = 0f;

            _lastLocked = locked;
        }

        private void TrackGnomeFeedback()
        {
            var gnome = Petrifiable.Local;
            if (gnome != _trackedGnome)
            {
                _trackedGnome = gnome;
                _lastPresses = gnome != null ? gnome.PressCount : 0;
                _lastReleases = gnome != null ? gnome.ReleaseCount : 0;
                return;
            }

            if (gnome == null)
                return;

            if (gnome.PressCount != _lastPresses)
            {
                _lastPresses = gnome.PressCount;
                if (_valid)
                    _titlePunch.Shake(_shakePixels, _shakeDuration);
                if (CameraPunch.Instance != null)
                    CameraPunch.Instance.PunchFov(0.6f, 0.08f);
            }

            if (gnome.ReleaseCount == _lastReleases)
                return;

            _lastReleases = gnome.ReleaseCount;
            if (ScreenFlash.Instance != null)
                ScreenFlash.Instance.Flash(_releaseFlashColor, 0.15f);
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(4f, 0.2f);
        }

        /// <summary>Sizes the screen-wide vignettes from the canvas rect; only runs when the canvas size changes.</summary>
        private void RefreshLayout()
        {
            var size = _canvasRect.rect.size;
            if (size == _lastCanvasSize)
                return;

            _lastCanvasSize = size;
            SizeVignette(_gnomeVignette.rectTransform, size, _warningInnerRadius);
            SizeVignette(_focusVignette.rectTransform, size, _focusVignetteInner);
            _lastConeHalfAngle = -1f;
        }

        // The clear centre of the sprite is an ellipse that covers `inner` of the half screen on each axis.
        private static void SizeVignette(RectTransform rect, Vector2 canvasSize, float inner)
        {
            rect.sizeDelta = new Vector2(
                HudSupport.VignetteSizeFor(inner * canvasSize.x * 0.5f),
                HudSupport.VignetteSizeFor(inner * canvasSize.y * 0.5f));
        }

        private void UpdateGnome(Petrifiable gnome)
        {
            if (gnome == null)
            {
                HudSupport.SetEnabled(_gnomeVignette, false);
                HudSupport.SetActive(_stoneRoot, false);
                return;
            }

            if (!gnome.IsStone)
            {
                var color = gnome.IsLockTarget ? _warningColor : _coneWarningColor;
                color.a *= gnome.WarningProgress;
                _gnomeVignette.color = color;
                HudSupport.SetEnabled(_gnomeVignette, color.a > 0.001f);
                HudSupport.SetActive(_stoneRoot, false);
                _lastCounterKey = -1;
                return;
            }

            _gnomeVignette.color = _petrifiedColor;
            HudSupport.SetEnabled(_gnomeVignette, true);
            HudSupport.SetActive(_stoneRoot, true);
            UpdateBreakFree(gnome);
        }

        private void UpdateBreakFree(Petrifiable gnome)
        {
            var presses = Mathf.FloorToInt(gnome.BreakProgress);
            var required = Mathf.CeilToInt(gnome.RequiredPresses);
            var key = presses * 10000 + required;
            if (key != _lastCounterKey)
            {
                _lastCounterKey = key;
                // TMP formats numbers as floats, so the format asks for zero decimals; no string is allocated.
                _stoneCounter.SetText("{0:0}/{1:0}", presses, required);
            }

            var fill = gnome.BreakFraction;
            if (!Mathf.Approximately(fill, _lastBreakFill))
            {
                _lastBreakFill = fill;
                HudSupport.SetFill(_stoneBarFill, fill);
            }
        }

        private void UpdateCamper(CamperGaze gaze)
        {
            if (gaze == null)
            {
                HudSupport.SetEnabled(_focusVignette, false);
                HudSupport.SetEnabled(_coneVignette, false);
                HudSupport.SetActive(_staminaRoot, false);
                return;
            }

            var focus = CameraFocus.Instance;
            var amount = focus != null ? focus.Amount : 0f;
            if (amount > 0.001f)
            {
                var tint = _focusVignetteColor;
                tint.a *= amount;
                _focusVignette.color = tint;
                HudSupport.SetEnabled(_focusVignette, true);
            }
            else
            {
                HudSupport.SetEnabled(_focusVignette, false);
            }

            UpdateStamina(gaze);
            UpdateCone(gaze);
        }

        private void UpdateCone(CamperGaze gaze)
        {
            if (!gaze.Focusing)
            {
                HudSupport.SetEnabled(_coneVignette, false);
                return;
            }

            if (_camera == null)
                _camera = Camera.main;

            // Matches the cone's clear centre to the gaze radius; only resized when the angle, the FOV or the canvas change.
            var fov = _camera != null ? _camera.fieldOfView : 60f;
            var halfAngle = gaze.HalfAngle;
            if (!Mathf.Approximately(halfAngle, _lastConeHalfAngle) || !Mathf.Approximately(fov, _lastConeFov))
            {
                _lastConeHalfAngle = halfAngle;
                _lastConeFov = fov;
                var radius = Mathf.Tan(halfAngle * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * _lastCanvasSize.y * 0.5f;
                var side = HudSupport.VignetteSizeFor(radius);
                _coneVignette.rectTransform.sizeDelta = new Vector2(side, side);
            }

            var alpha = _coneVignetteAlpha;
            if (_pulseTime < _pulseDuration)
                alpha = Mathf.Lerp(_pulseAlpha, _coneVignetteAlpha, _pulseTime / _pulseDuration);

            var color = _coneColor;
            color.a = alpha;
            _coneVignette.color = color;
            HudSupport.SetEnabled(_coneVignette, true);
        }

        private void UpdateStamina(CamperGaze gaze)
        {
            var gazeOn = gaze.IsGazeOn;
            HudSupport.SetActive(_staminaRoot, gazeOn);
            if (!gazeOn)
                return;

            var exhausted = gaze.Exhausted;
            var color = _staminaColor;
            if (exhausted)
            {
                var on = Mathf.Repeat(Time.time * _exhaustedBlinkHz, 1f) < 0.5f;
                color = _exhaustedColor;
                color.a *= on ? 1f : 0.35f;
            }

            _staminaFillImage.color = color;

            var fraction = gaze.StaminaFraction;
            if (!Mathf.Approximately(fraction, _lastStaminaFill))
            {
                _lastStaminaFill = fraction;
                HudSupport.SetFill(_staminaFill, fraction);
            }

            var threshold = gaze.ReuseThreshold;
            if (!Mathf.Approximately(threshold, _lastThreshold))
            {
                _lastThreshold = threshold;
                var min = _staminaThreshold.anchorMin;
                var max = _staminaThreshold.anchorMax;
                min.x = threshold;
                max.x = threshold;
                _staminaThreshold.anchorMin = min;
                _staminaThreshold.anchorMax = max;
            }

            HudSupport.SetActive(_exhaustedLabel.gameObject, exhausted);
            HudSupport.SetActive(_hintLabel.gameObject, !exhausted && !gaze.Focusing && fraction >= 0.999f);
        }
    }
}
