using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Shared helpers for the in-game HUD scripts (C0.7): the geometry of the radial vignette sprite, the check for
    /// missing serialized references and allocation-free visibility/fill setters.
    /// </summary>
    public static class HudSupport
    {
        // Geometry of Art/UI/vignette_radial.png, which GameHudBuilder generates. The sprite is 9-sliced: an opaque
        // border ring around a centre cell that holds the radial gradient (alpha 0 up to ClearRadiusPixels, then a
        // smooth ramp to 1 at RampEndPixels). Stretching the Image stretches the centre cell only, so the clear
        // radius can be set in canvas units while the border keeps covering whatever lies outside the Image.
        public const int SpritePixels = 512;
        public const int BorderPixels = 128;
        public const int CellPixels = SpritePixels - 2 * BorderPixels;
        public const float ClearRadiusPixels = 56f;
        public const float RampEndPixels = 112f;

        /// <summary>
        /// Image.pixelsPerUnitMultiplier for the vignettes: it scales the opaque border up so it reaches far past the
        /// screen edges at any aspect ratio. Assumes sprite pixels per unit 100 and a canvas reference of 100.
        /// </summary>
        public const float BorderMultiplier = 0.1f;

        /// <summary>Thickness, in canvas units, of the opaque border ring of a vignette Image.</summary>
        public const float BorderUnits = BorderPixels / BorderMultiplier;

        /// <summary>Image size (one axis, canvas units) whose transparent centre has radius <paramref name="clearRadius"/>.</summary>
        public static float VignetteSizeFor(float clearRadius) =>
            clearRadius * CellPixels / ClearRadiusPixels + 2f * BorderUnits;

        /// <summary>Logs an error and returns false when a serialized reference was not wired.</summary>
        public static bool Require(Object owner, Object reference, string field)
        {
            if (reference != null)
                return true;

            Debug.LogError($"[{owner.GetType().Name}] Missing reference '{field}' on '{owner.name}'. " +
                           "Run the menu GnoMore/Construir HUD de juego and, if this is a scene copy, remove this component.", owner);
            return false;
        }

        /// <summary>Activates or deactivates <paramref name="target"/> only when its state changes.</summary>
        public static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf != active)
                target.SetActive(active);
        }

        /// <summary>Enables or disables <paramref name="target"/> only when its state changes.</summary>
        public static void SetEnabled(Behaviour target, bool enabled)
        {
            if (target.enabled != enabled)
                target.enabled = enabled;
        }

        /// <summary>Sets the fill of a bar made of a stretched child: its right anchor moves, offsets stay at zero.</summary>
        public static void SetFill(RectTransform fill, float fraction)
        {
            var max = fill.anchorMax;
            max.x = Mathf.Clamp01(fraction);
            fill.anchorMax = max;
        }
    }
}
