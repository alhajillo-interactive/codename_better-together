using BetterTogether.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.UI
{
    /// <summary>
    /// Small on-screen badge telling the local player whether their microphone is on.
    /// Drives a badge on the HUD canvas; the text and colour change only when the state changes.
    /// </summary>
    public class MicIndicator : MonoBehaviour
    {
        private const string OnText = "MIC ON   [M]";
        private const string OffText = "MIC OFF   [M]";

        [Header("References (wired by GameHudBuilder)")]
        [Tooltip("Root of the badge; hidden while there is no local voice.")]
        [SerializeField] private GameObject _badge;
        [Tooltip("Background of the badge, tinted by the microphone state.")]
        [SerializeField] private Image _background;
        [Tooltip("Label of the badge.")]
        [SerializeField] private TMP_Text _label;

        [Header("Style")]
        [Tooltip("Background colour while the microphone is on.")]
        [SerializeField] private Color _onColor = new(0.2f, 0.7f, 0.3f, 0.9f);
        [Tooltip("Background colour while the microphone is off.")]
        [SerializeField] private Color _offColor = new(0.8f, 0.2f, 0.2f, 0.9f);

        // The theme (leaf / hat) wins when it exists; the serialized colours are the fallback.
        private Color OnColor => UiTheme.Current != null ? WithAlpha(UiTheme.Current.leaf, _onColor.a) : _onColor;
        private Color OffColor => UiTheme.Current != null ? WithAlpha(UiTheme.Current.hat, _offColor.a) : _offColor;

        private bool _valid;
        private int _shownState = -1;

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void Awake()
        {
            _valid = HudSupport.Require(this, _badge, nameof(_badge))
                     & HudSupport.Require(this, _background, nameof(_background))
                     & HudSupport.Require(this, _label, nameof(_label));

            if (_valid)
                _badge.SetActive(false);
        }

        private void Update()
        {
            if (!_valid)
                return;

            var voice = PlayerVoice.Local;
            if (voice == null)
            {
                HudSupport.SetActive(_badge, false);
                _shownState = -1;
                return;
            }

            HudSupport.SetActive(_badge, true);
            var state = voice.IsMicOn ? 1 : 0;
            if (state == _shownState)
                return;

            _shownState = state;
            _background.color = state == 1 ? OnColor : OffColor;
            _label.text = state == 1 ? OnText : OffText;
        }
    }
}
