using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Tiny tween for a UI element: scale pop/punch and horizontal shake, on unscaled time.
    /// Restores the original scale and position when it finishes or is disabled.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPunch : MonoBehaviour
    {
        private const float PeakPortion = 0.6f;

        private RectTransform _rect;
        private Vector3 _baseScale;
        private Vector2 _basePosition;
        private bool _ready;

        private bool _scaling;
        private float _scaleElapsed;
        private float _scaleDuration;
        private float _scaleFrom;
        private float _scalePeak;

        private bool _shaking;
        private float _shakeElapsed;
        private float _shakeDuration;
        private float _shakeStrength;

        /// <summary>Returns the <see cref="UIPunch"/> on <paramref name="target"/>, adding one if needed.</summary>
        public static UIPunch Of(Component target)
        {
            if (target.TryGetComponent<UIPunch>(out var punch))
                return punch;

            return target.gameObject.AddComponent<UIPunch>();
        }

        /// <summary>Scales from <paramref name="from"/> up to <paramref name="peak"/> then settles at 1.</summary>
        public void Pop(float from, float peak, float duration)
        {
            EnsureReady();
            _scaling = true;
            _scaleElapsed = 0f;
            _scaleDuration = Mathf.Max(0.01f, duration);
            _scaleFrom = from;
            _scalePeak = peak;
        }

        /// <summary>Scales 1, <paramref name="peak"/>, 1.</summary>
        public void Punch(float peak, float duration) => Pop(1f, peak, duration);

        /// <summary>Shakes sideways by up to <paramref name="strength"/> pixels, fading out.</summary>
        public void Shake(float strength, float duration)
        {
            EnsureReady();
            _shaking = true;
            _shakeElapsed = 0f;
            _shakeDuration = Mathf.Max(0.01f, duration);
            _shakeStrength = strength;
        }

        private void EnsureReady()
        {
            if (_ready)
                return;

            _rect = (RectTransform)transform;
            _baseScale = _rect.localScale;
            _basePosition = _rect.anchoredPosition;
            _ready = true;
        }

        private void OnDisable()
        {
            if (!_ready)
                return;

            _scaling = false;
            _shaking = false;
            _rect.localScale = _baseScale;
            _rect.anchoredPosition = _basePosition;
        }

        private void Update()
        {
            if (!_ready)
                return;

            var delta = Time.unscaledDeltaTime;

            if (_scaling)
            {
                _scaleElapsed += delta;
                var t = _scaleElapsed / _scaleDuration;
                if (t >= 1f)
                {
                    _scaling = false;
                    _rect.localScale = _baseScale;
                }
                else
                {
                    _rect.localScale = _baseScale * EvaluateScale(t);
                }
            }

            if (_shaking)
            {
                _shakeElapsed += delta;
                var t = _shakeElapsed / _shakeDuration;
                if (t >= 1f)
                {
                    _shaking = false;
                    _rect.anchoredPosition = _basePosition;
                }
                else
                {
                    var offset = Mathf.Sin(_shakeElapsed * 70f) * _shakeStrength * (1f - t);
                    _rect.anchoredPosition = _basePosition + new Vector2(offset, 0f);
                }
            }
        }

        private float EvaluateScale(float t)
        {
            if (t < PeakPortion)
            {
                var u = t / PeakPortion;
                var eased = 1f - (1f - u) * (1f - u) * (1f - u);
                return Mathf.LerpUnclamped(_scaleFrom, _scalePeak, eased);
            }

            var v = (t - PeakPortion) / (1f - PeakPortion);
            return Mathf.Lerp(_scalePeak, 1f, Mathf.SmoothStep(0f, 1f, v));
        }
    }
}
