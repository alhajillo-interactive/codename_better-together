using TMPro;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Makes a plate (header, toast pill) grow with the text it holds: width follows the text up to a maximum, and when the
    /// text is longer than that the font shrinks (down to a minimum) instead of overflowing the plate. Refresh is explicit
    /// (call it after changing the text) and runs on events only, never per frame.
    /// </summary>
    public class ElasticPlate : MonoBehaviour
    {
        [Header("References (wired by UiThemeApplier)")]
        [Tooltip("Plate that is resized.")]
        [SerializeField] private RectTransform _plate;
        [Tooltip("Text inside the plate (a child of it).")]
        [SerializeField] private TMP_Text _text;

        [Header("Layout")]
        [Tooltip("Horizontal padding between the plate edge and the text.")]
        [SerializeField] private float _paddingX = 24f;
        [Tooltip("Vertical padding between the plate edge and the text (only when Fit Height is on).")]
        [SerializeField] private float _paddingY = 8f;
        [Tooltip("Plate width never goes below this.")]
        [SerializeField] private float _minWidth = 120f;
        [Tooltip("Plate width never goes above this; longer texts shrink the font instead.")]
        [SerializeField] private float _maxWidth = 520f;
        [Tooltip("Also size the plate height from the text (toasts). Off keeps the height the plate already has.")]
        [SerializeField] private bool _fitHeight;
        [Tooltip("Plate height never goes below this when Fit Height is on.")]
        [SerializeField] private float _minHeight = 64f;

        [Header("Font")]
        [Tooltip("Font size used when the text fits. 0 reads the size the text has on Awake.")]
        [SerializeField] private float _maxFontSize;
        [Tooltip("The font never shrinks below this.")]
        [SerializeField] private float _minFontSize = 24f;

        private string _lastText;
        private bool _hasBase;

        private void Awake() => EnsureBase();

        private void OnEnable() => Refresh();

        /// <summary>Re-measures the text and resizes the plate. Cheap enough for events (a title change, a toast).</summary>
        public void Refresh()
        {
            if (_plate == null || _text == null)
                return;

            EnsureBase();
            var content = _text.text;
            if (_lastText != null && string.Equals(content, _lastText))
                return;

            _lastText = content;

            // Measure at the full size, without TMP autosize or wrapping, so the width is the real one-line width.
            _text.enableAutoSizing = false;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.fontSize = _maxFontSize;
            var preferred = _text.GetPreferredValues(content);

            var width = Mathf.Clamp(preferred.x + _paddingX * 2f, _minWidth, _maxWidth);
            var inner = width - _paddingX * 2f;
            var size = _maxFontSize;
            if (preferred.x > inner && preferred.x > 0f)
                size = Mathf.Max(_minFontSize, _maxFontSize * inner / preferred.x);

            _text.fontSize = size;
            _plate.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (_fitHeight)
            {
                var height = Mathf.Max(_minHeight, _text.GetPreferredValues(content).y + _paddingY * 2f);
                _plate.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            // The text fills the plate minus the padding, so it stays inside whatever the plate does.
            var rect = _text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(_paddingX, _fitHeight ? _paddingY : 0f);
            rect.offsetMax = new Vector2(-_paddingX, _fitHeight ? -_paddingY : 0f);
        }

        private void EnsureBase()
        {
            if (_hasBase || _text == null)
                return;

            _hasBase = true;
            if (_maxFontSize <= 0f)
                _maxFontSize = _text.fontSize;
        }
    }
}
