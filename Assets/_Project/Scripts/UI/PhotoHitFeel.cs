using BetterTogether.Audio;
using BetterTogether.Gaze;
using BetterTogether.Player;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// C9.9: when the local camper's photo lands (a new <see cref="CamperGaze.ShotSeq"/> with <see cref="CamperGaze.LastShotHit"/>)
    /// a white photo frame closes over the focused gnome for 150 ms, with a short FOV punch. A visual beat only: the game is
    /// never paused (no Time.timeScale). A miss shows nothing extra (the normal shutter flash of <see cref="GazeFeedbackHUD"/> remains).
    /// Detected from replicated state in Update, so a late joiner or a master change never replays an old shot (the first look only
    /// records the sequence). "Reducir destellos" halves the frame opacity and the FOV punch. Nothing networked, nothing allocated per frame.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class PhotoHitFeel : MonoBehaviour
    {
        [Tooltip("Centered RectTransform whose size is the frame (4 edge bars as children). Wired by GnoMore/Construir feel núcleo.")]
        [SerializeField] private RectTransform _frame;
        [Tooltip("CanvasGroup on the frame (its alpha fades the frame out).")]
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Seconds the frame takes to close over the gnome.")]
        [SerializeField, Min(0.02f)] private float _duration = 0.15f;
        [Tooltip("Seconds the frame stays at the end before it disappears (it fades during the last part).")]
        [SerializeField, Min(0f)] private float _hold = 0.08f;
        [Tooltip("Half size (canvas units) of the frame when it appears.")]
        [SerializeField, Min(50f)] private float _startHalf = 520f;
        [Tooltip("Size of the closed frame around the gnome, in metres of the gnome's height (about 0.5 m).")]
        [SerializeField, Min(0.1f)] private float _endHalfMetres = 0.35f;
        [Tooltip("Limits (canvas units) of the half size of the closed frame, so it stays readable at any distance.")]
        [SerializeField] private Vector2 _endHalfRange = new(70f, 220f);
        [Tooltip("Degrees the FOV changes on a hit (negative = narrows, as if the frame zoomed in).")]
        [SerializeField] private float _fovPunch = -3f;
        [Tooltip("Seconds the FOV punch takes to ease back.")]
        [SerializeField, Min(0.02f)] private float _fovPunchTime = 0.15f;
        [Tooltip("Opacity multiplier of the frame and the FOV punch with 'Reducir destellos'.")]
        [SerializeField, Range(0f, 1f)] private float _reducedScale = 0.5f;
        [Tooltip("Played for the camper when a photo lands. Empty = silent (for the audio team).")]
        [SerializeField] private SoundCue _hitSfx;

        private RectTransform _canvasRect;
        private Camera _camera;
        private CamperGaze _tracked;
        private byte _lastShotSeq;
        private bool _valid;
        private bool _active;
        private float _start;
        private float _peakAlpha;
        private Petrifiable _target;
        private Petrifiable _lastLocked;
        private Vector2 _lastPosition;
        private float _lastEndHalf;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _frame, nameof(_frame)) && HudSupport.Require(this, _group, nameof(_group));
            if (!_valid)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                _valid = false;
                return;
            }

            _canvasRect = (RectTransform)canvas.rootCanvas.transform;
            _group.alpha = 0f;
            _frame.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            _active = false;
            if (_valid)
                _frame.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_valid)
                return;

            var gaze = CamperGaze.Local;
            if (gaze != _tracked)
            {
                // First look at this camper (spawn, late join, master change): record the sequence only.
                _tracked = gaze;
                _active = false;
                _frame.gameObject.SetActive(false);
                if (gaze != null)
                    _lastShotSeq = gaze.ShotSeq;

                return;
            }

            if (gaze == null)
                return;

            // The shot can release the lock in the same tick, so the frame needs the last gnome that was focused.
            var locked = gaze.LockedTarget;
            if (locked != null)
                _lastLocked = locked;

            var seq = gaze.ShotSeq;
            if (seq == _lastShotSeq)
                return;

            _lastShotSeq = seq;
            if (gaze.LastShotHit)
                Begin(gaze);
        }

        private void Begin(CamperGaze gaze)
        {
            var reduced = FlashSettings.ReduceFlashes;
            _peakAlpha = reduced ? _reducedScale : 1f;
            _target = gaze.LockedTarget != null ? gaze.LockedTarget : _lastLocked;
            _start = Time.unscaledTime;
            _active = true;
            _lastEndHalf = _endHalfRange.y;
            _lastPosition = Vector2.zero;
            _frame.gameObject.SetActive(true);
            Layout(0f);
            _hitSfx.Play();
            if (CameraPunch.Instance != null)
                CameraPunch.Instance.PunchFov(reduced ? _fovPunch * _reducedScale : _fovPunch, _fovPunchTime);
        }

        private void LateUpdate()
        {
            if (!_active)
                return;

            var age = Time.unscaledTime - _start;
            if (age >= _duration + _hold)
            {
                _active = false;
                _group.alpha = 0f;
                _frame.gameObject.SetActive(false);
                return;
            }

            Layout(age);
        }

        // The frame follows the gnome's focus point while it closes; it fades over the last third of the hold.
        private void Layout(float age)
        {
            if (_camera == null)
                _camera = Camera.main;

            var closing = Mathf.Clamp01(age / _duration);
            var ease = 1f - (1f - closing) * (1f - closing);
            var size = _canvasRect.rect.size;
            if (_camera != null && _target != null)
            {
                var screen = _camera.WorldToScreenPoint(_target.FocusPoint);
                if (screen.z > 0.05f)
                {
                    var toCanvas = size.x / Mathf.Max(1f, _camera.pixelWidth);
                    _lastPosition = new Vector2(
                        (screen.x - _camera.pixelWidth * 0.5f) * toCanvas,
                        (screen.y - _camera.pixelHeight * 0.5f) * toCanvas);
                    var tanHalfV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    var perMetre = size.y * 0.5f / (screen.z * tanHalfV);
                    _lastEndHalf = Mathf.Clamp(_endHalfMetres * perMetre, _endHalfRange.x, _endHalfRange.y);
                }
            }

            var half = Mathf.Lerp(_startHalf, _lastEndHalf, ease);
            _frame.anchoredPosition = _lastPosition;
            _frame.sizeDelta = new Vector2(half * 2f, half * 2f);

            var fadeStart = _duration + _hold * 0.35f;
            var fade = age <= fadeStart ? 1f : 1f - Mathf.Clamp01((age - fadeStart) / Mathf.Max(0.01f, _duration + _hold - fadeStart));
            _group.alpha = _peakAlpha * Mathf.Clamp01(age / 0.03f + 0.4f) * fade;
        }
    }
}
