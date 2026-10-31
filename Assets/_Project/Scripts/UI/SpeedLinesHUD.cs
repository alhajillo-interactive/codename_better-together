using BetterTogether.Player;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// C9.9: thin cream wind strokes drawn at the screen edges while the local player sprints on the ground. The amount comes
    /// from <see cref="SprintFeel.SpeedFxAmount"/> (already faded in 200 ms and zero in the water, in the air or with the camera
    /// raised); this component only draws it. Strokes stay outside the central 60 % of the screen. Off with the setting
    /// "Efectos de velocidad: no" (<see cref="SpeedFxSettings"/>). Local presentation: nothing networked, nothing allocated per frame.
    /// A late joiner, a master change or a player leaving changes nothing here (only the local player drives it).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class SpeedLinesHUD : MonoBehaviour
    {
        [Tooltip("Stretched root of the strokes (child of the HUD canvas). Wired by GnoMore/Construir feel núcleo.")]
        [SerializeField] private RectTransform _root;
        [Tooltip("The 8 to 12 stroke Images (thin rectangles, no sprite) under the root.")]
        [SerializeField] private Image[] _strokes;
        [Tooltip("Strokes never come closer to the centre than this fraction of the half screen (the central 60 % stays clear).")]
        [SerializeField, Range(0.62f, 0.95f)] private float _innerFraction = 0.78f;
        [Tooltip("Fraction of the half screen where a stroke ends its run (above 1 = past the edge).")]
        [SerializeField, Range(0.9f, 1.2f)] private float _outerFraction = 1.04f;
        [Tooltip("Peak opacity of a stroke at full speed amount.")]
        [SerializeField, Range(0f, 1f)] private float _peakAlpha = 0.25f;
        [Tooltip("Stroke length range (canvas units).")]
        [SerializeField] private Vector2 _lengthRange = new(70f, 120f);
        [Tooltip("Stroke thickness (canvas units).")]
        [SerializeField, Min(1f)] private float _thickness = 3f;
        [Tooltip("Runs per second of a stroke from inner to outer.")]
        [SerializeField, Min(0.1f)] private float _runsPerSecond = 2.2f;

        private RectTransform _canvasRect;
        private bool _valid;
        private float[] _angle;
        private float[] _phase;
        private float[] _length;
        private bool _shown;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _root, nameof(_root)) && _strokes != null && _strokes.Length > 0;
            if (!_valid)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                _valid = false;
                return;
            }

            _canvasRect = (RectTransform)canvas.rootCanvas.transform;
            var count = _strokes.Length;
            _angle = new float[count];
            _phase = new float[count];
            _length = new float[count];
            for (var i = 0; i < count; i++)
            {
                // Even spread around the screen with a fixed pseudo-random wobble (no Random: the same look on every run).
                var wobble = Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.5453f, 1f) - 0.5f;
                _angle[i] = (i + 0.5f + wobble * 0.6f) / count * Mathf.PI * 2f;
                _phase[i] = Mathf.Repeat(i * 0.618034f, 1f);
                _length[i] = Mathf.Lerp(_lengthRange.x, _lengthRange.y, Mathf.Repeat(i * 0.37f + 0.2f, 1f));
                if (_strokes[i] == null)
                {
                    _valid = false;
                    return;
                }

                _strokes[i].raycastTarget = false;
                _strokes[i].rectTransform.sizeDelta = new Vector2(_length[i], _thickness);
            }

            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_valid)
                return;

            var feel = SprintFeel.Local;
            var amount = feel != null && SpeedFxSettings.Enabled ? feel.SpeedFxAmount : 0f;
            var show = amount > 0.001f;
            if (show != _shown)
            {
                _shown = show;
                _root.gameObject.SetActive(show);
            }

            if (!show)
                return;

            var size = _canvasRect.rect.size;
            var halfW = size.x * 0.5f;
            var halfH = size.y * 0.5f;
            var cream = UiTheme.Safe.cream;
            for (var i = 0; i < _strokes.Length; i++)
            {
                var run = Mathf.Repeat(Time.unscaledTime * _runsPerSecond + _phase[i], 1f);
                var f = Mathf.Lerp(_innerFraction, _outerFraction, run);
                var dx = Mathf.Cos(_angle[i]) * halfW;
                var dy = Mathf.Sin(_angle[i]) * halfH;
                var rect = _strokes[i].rectTransform;
                rect.anchoredPosition = new Vector2(dx * f, dy * f);
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                cream.a = _peakAlpha * amount * Mathf.Sin(run * Mathf.PI);
                _strokes[i].color = cream;
            }
        }
    }
}
