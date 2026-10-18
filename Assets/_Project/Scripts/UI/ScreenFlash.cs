using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Full-screen colour tint that fades out, for hit feedback. IMGUI placeholder until the game has a real HUD.
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        public static ScreenFlash Instance { get; private set; }

        private Color _color;
        private float _duration;
        private float _elapsed = float.MaxValue;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Tints the screen with <paramref name="color"/> (its alpha is the peak) and fades it out.</summary>
        public void Flash(Color color, float duration)
        {
            _color = color;
            _duration = Mathf.Max(0.01f, duration);
            _elapsed = 0f;
        }

        private void Update() => _elapsed += Time.unscaledDeltaTime;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _elapsed >= _duration)
                return;

            var previous = GUI.color;
            var color = _color;
            color.a *= 1f - _elapsed / _duration;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
