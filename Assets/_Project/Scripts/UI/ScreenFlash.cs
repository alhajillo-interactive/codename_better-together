using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Full-screen colour tint that fades out, for hit feedback. Drives a full-screen Image on the HUD canvas.
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        [Tooltip("Full-screen Image that is tinted and faded out. Wired by GameHudBuilder.")]
        [SerializeField] private Image _image;

        public static ScreenFlash Instance { get; private set; }

        private Tween _tween;
        private bool _active;
        private bool _valid;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _image, nameof(_image));
            if (!_valid)
                return;

            _image.raycastTarget = false;
            _image.enabled = false;
            Instance = this;
        }

        private void OnDestroy()
        {
            _tween.Stop();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Tints the screen with <paramref name="color"/> (its alpha is the peak) and fades it out.</summary>
        public void Flash(Color color, float duration)
        {
            if (!_valid)
                return;

            _tween.Stop();
            _image.color = color;
            _image.enabled = true;
            _active = true;
            _tween = Tween.Alpha(_image, color.a, 0f, Mathf.Max(0.01f, duration), Ease.Linear, useUnscaledTime: true);
        }

        private void Update()
        {
            if (!_active || _tween.isAlive)
                return;

            _active = false;
            _image.enabled = false;
        }
    }
}
