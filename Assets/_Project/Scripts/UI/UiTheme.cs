using System.Collections.Generic;
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

        private static readonly Dictionary<int, string> HexCache = new();

        private static UiTheme _current;
        private static UiTheme _fallback;
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
        [Tooltip("Legacy confirmation green. Superseded by leafDark (plan v2 palette): runtime scripts read leafDark; the field stays so the serialized asset keeps loading.")]
        public Color leaf = new Color32(0x6B, 0xA5, 0x39, 0xFF);
        [Tooltip("Legacy danger red. Superseded by hatDark (plan v2 palette): runtime scripts read hatDark; the field stays so the serialized asset keeps loading.")]
        public Color hat = new Color32(0xB8, 0x42, 0x3A, 0xFF);
        [Tooltip("Focus, timer, slider fill.")]
        public Color ember = new Color32(0xF6, 0xC3, 0x43, 0xFF);
        [Tooltip("Dim behind modals (alpha included).")]
        public Color shade = new Color(0x2B / 255f, 0x1A / 255f, 0x10 / 255f, 0.55f);

        [Header("Colours v2 (semantic palette, Docs/plan/fase-6/plan-ux-ui.md section 3)")]
        [Tooltip("Secondary text and placeholders over paper; also a finished objective line.")]
        public Color inkSoft = new Color32(0x7A, 0x4A, 0x2E, 0xFF);
        [Tooltip("Pill behind text over the scene (prompt, toasts); cream text on it reads at 8:1 in the worst case.")]
        public Color night80 = new Color(0x2B / 255f, 0x1A / 255f, 0x10 / 255f, 0.8f);
        [Tooltip("Green text over dark backgrounds (ready chip in the lobby). leafDark is for fills with cream text.")]
        public Color leafLight = new Color32(0x7C, 0xE3, 0x8B, 0xFF);
        [Tooltip("Confirm / ready / positive fill. One primary button per screen, positive toasts. Cream text on it: 4.79:1.")]
        public Color leafDark = new Color32(0x3F, 0x7A, 0x22, 0xFF);
        [Tooltip("Danger, failure, exit. Cream text on it: 6.66:1.")]
        public Color hatDark = new Color32(0x9E, 0x2F, 0x28, 0xFF);
        [Tooltip("Camper role colour (campfire light). Always with an icon; reads on dark backgrounds.")]
        public Color roleCamper = new Color32(0xFF, 0xB5, 0x47, 0xFF);
        [Tooltip("Gnome role colour (night forest blue). Cream text on it: 4.77:1.")]
        public Color roleGnome = new Color32(0x3D, 0x6F, 0xA8, 0xFF);

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
        [Tooltip("Light, neutral copy of the wood button for the tinted variants (confirm, danger), so the tint reads clean.")]
        public Sprite buttonTint;
        [Tooltip("Paper disc with a woodDark rim; 9-sliced it is the rounded text field background and the slider handle.")]
        public Sprite knob;
        [Tooltip("White rounded pill, tinted for slider tracks and fills.")]
        public Sprite pill;
        [Tooltip("Menu illustration (MainMenuBG).")]
        public Sprite backdrop;
        [Tooltip("Ink stamp: ring with a check mark (objective done).")]
        public Sprite stamp;
        [Tooltip("Ink stamp: ring with a cross, drawn in hatDark (objective failed).")]
        public Sprite stampFail;

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

        /// <summary>
        /// The theme in Resources/UiTheme, or a hidden instance with the default values when there is none, so callers
        /// can read colours without a null check.
        /// </summary>
        public static UiTheme Safe
        {
            get
            {
                var theme = Current;
                if (theme != null)
                    return theme;

                if (_fallback == null)
                {
                    _fallback = CreateInstance<UiTheme>();
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallback;
            }
        }

        /// <summary>
        /// "#RRGGBB" (or "#RRGGBBAA" when translucent) for rich text. Strings are cached per colour, so after the first
        /// call for a colour this does not allocate and is safe to use in refresh code.
        /// </summary>
        public static string Hex(Color color)
        {
            Color32 c = color;
            var key = (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;
            if (!HexCache.TryGetValue(key, out var hex))
            {
                hex = c.a == 255
                    ? $"#{c.r:X2}{c.g:X2}{c.b:X2}"
                    : $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
                HexCache.Add(key, hex);
            }

            return hex;
        }

        /// <summary>The colour with another alpha (value type copy, no allocation).</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _fallback = null;
            _searched = false;
        }
    }
}
