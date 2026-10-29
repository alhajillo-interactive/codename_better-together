using TMPro;
using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Single source of truth for the "wood and paper camp" UI style (see Docs/plan/fase-6/UI-estilo.md): colour tokens,
    /// fonts, sprites and text sizes. The asset lives in Resources/UiTheme and is created and wired by
    /// GnoMore/Aplicar estilo de UI; runtime scripts read <see cref="Current"/> instead of hard-coding colours.
    /// </summary>
    [CreateAssetMenu(fileName = "UiTheme", menuName = "Better Together/UI/Ui Theme")]
    public class UiTheme : ScriptableObject
    {
        private const string ResourcePath = "UiTheme";

        private static UiTheme _current;
        private static bool _searched;

        [Header("Colours")]
        [Tooltip("Borders, button shadow, slider track.")]
        public Color woodDark = new Color32(0xA8, 0x43, 0x1A, 0xFF);
        [Tooltip("Buttons, headers.")]
        public Color wood = new Color32(0xC2, 0x62, 0x2F, 0xFF);
        [Tooltip("Button fill, highlighted rows.")]
        public Color woodLight = new Color32(0xEF, 0xB5, 0x77, 0xFF);
        [Tooltip("Panels, cards, text fields.")]
        public Color paper = new Color32(0xF9, 0xEA, 0xC8, 0xFF);
        [Tooltip("Text over paper or light wood.")]
        public Color ink = new Color32(0x5E, 0x2C, 0x12, 0xFF);
        [Tooltip("Text over wood or over the game (with an ink outline).")]
        public Color cream = new Color32(0xFF, 0xF4, 0xDE, 0xFF);
        [Tooltip("Confirmation, ready, chosen option, gnomes.")]
        public Color leaf = new Color32(0x6B, 0xA5, 0x39, 0xFF);
        [Tooltip("Danger, exit, camper.")]
        public Color hat = new Color32(0xB8, 0x42, 0x3A, 0xFF);
        [Tooltip("Focus, timer, slider fill.")]
        public Color ember = new Color32(0xF6, 0xC3, 0x43, 0xFF);
        [Tooltip("Dim behind modals (alpha included).")]
        public Color shade = new Color(0x2B / 255f, 0x1A / 255f, 0x10 / 255f, 0.55f);

        [Header("Fonts")]
        [Tooltip("Titles and buttons. Swap here (e.g. Lilita One) and re-run the tool.")]
        public TMP_FontAsset titleFont;
        [Tooltip("Body text. Swap here (e.g. Fredoka SemiBold) and re-run the tool.")]
        public TMP_FontAsset bodyFont;
        [Tooltip("Title font with the ink outline applied (material preset created by the tool).")]
        public Material titleOutline;
        [Tooltip("Body font with the ink outline applied (material preset created by the tool).")]
        public Material bodyOutline;
        [Tooltip("Outline width used when the tool creates the outline materials.")]
        [Range(0f, 1f)] public float outlineWidth = 0.25f;

        [Header("Sprites")]
        [Tooltip("Torn paper panel (tareas-bg).")]
        public Sprite panel;
        [Tooltip("Wooden frame with cream interior (Credits).")]
        public Sprite frame;
        [Tooltip("Terracotta header plate (InputHeader).")]
        public Sprite header;
        [Tooltip("Wooden button board (ButtonBG).")]
        public Sprite button;
        [Tooltip("Paper disc with a woodDark rim; 9-sliced it is the rounded text field background and the slider handle.")]
        public Sprite knob;
        [Tooltip("White rounded pill, tinted for slider tracks and fills.")]
        public Sprite pill;
        [Tooltip("Menu illustration (MainMenuBG).")]
        public Sprite backdrop;

        [Header("Text sizes at 1080p (reference, not forced on existing texts)")]
        [Tooltip("Title size.")]
        public float titleSize = 64f;
        [Tooltip("Subtitle size.")]
        public float subtitleSize = 40f;
        [Tooltip("Button label size.")]
        public float buttonSize = 34f;
        [Tooltip("Body text size.")]
        public float bodySize = 28f;
        [Tooltip("Hint text size.")]
        public float hintSize = 22f;

        /// <summary>The theme in Resources/UiTheme, or null when there is none (callers keep their own defaults).</summary>
        public static UiTheme Current
        {
            get
            {
                if (_current == null && !_searched)
                {
                    _searched = true;
                    _current = Resources.Load<UiTheme>(ResourcePath);
                }

                return _current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _searched = false;
        }
    }
}
