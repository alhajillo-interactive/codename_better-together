using BetterTogether.Voice;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Small on-screen badge telling the local player whether their microphone is on.
    /// IMGUI placeholder until the game has a real HUD.
    /// </summary>
    public class MicIndicator : MonoBehaviour
    {
        [SerializeField] private Color _onColor = new(0.2f, 0.7f, 0.3f, 0.9f);
        [SerializeField] private Color _offColor = new(0.8f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Vector2 _size = new(160f, 34f);
        [SerializeField] private float _margin = 12f;

        private GUIStyle _style;

        private void OnGUI()
        {
            var voice = PlayerVoice.Local;
            if (voice == null)
                return;

            _style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white, background = Texture2D.whiteTexture },
            };

            var micOn = voice.IsMicOn;
            var rect = new Rect(_margin, Screen.height - _size.y - _margin, _size.x, _size.y);
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = micOn ? _onColor : _offColor;
            GUI.Box(rect, micOn ? "MIC ON   [M]" : "MIC OFF   [M]", _style);
            GUI.backgroundColor = previousColor;
        }
    }
}
