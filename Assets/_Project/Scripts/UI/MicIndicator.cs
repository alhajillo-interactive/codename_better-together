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
        private const string OnText = "Micro abierto · M";
        private const string OffText = "Micro apagado · M";

        [Header("References (wired by GameHudBuilder)")]
        [Tooltip("Root of the badge; hidden while there is no local voice.")]
        [SerializeField] private GameObject _badge;
        [Tooltip("Background of the badge, tinted by the microphone state.")]
        [SerializeField] private Image _background;
        [Tooltip("Label of the badge.")]
        [SerializeField] private TMP_Text _label;

        [Header("Style")]
        [Tooltip("Opacity of the badge background (the colours come from the theme: leafDark on, hatDark off).")]
        [Range(0.5f, 1f)] [SerializeField] private float _backgroundAlpha = 0.95f;

        private Color OnColor => UiTheme.WithAlpha(UiTheme.Safe.leafDark, _backgroundAlpha);
        private Color OffColor => UiTheme.WithAlpha(UiTheme.Safe.hatDark, _backgroundAlpha);

        // Optional child "Icon" of the badge (made by HudPlateTools): MicOn / MicOff next to the text.
        private Image _icon;
        private bool _valid;
        private int _shownState = -1;

        private void Awake()
        {
            _valid = HudSupport.Require(this, _badge, nameof(_badge))
                     & HudSupport.Require(this, _background, nameof(_background))
                     & HudSupport.Require(this, _label, nameof(_label));

            if (!_valid)
                return;

            _label.color = UiTheme.Safe.cream; // cream on leafDark / hatDark
            var icon = _badge.transform.Find("Icon");
            if (icon != null && icon.TryGetComponent(out _icon))
                _icon.raycastTarget = false;

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
            if (_icon != null)
            {
                var sprite = UiTheme.Safe.Icon(state == 1 ? UiIcon.MicOn : UiIcon.MicOff);
                _icon.sprite = sprite;
                _icon.enabled = sprite != null;
            }
        }
    }
}
