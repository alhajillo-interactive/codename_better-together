using UnityEngine;

namespace BetterTogether.Diagnostics
{
    /// <summary>
    /// Shared scale for the IMGUI debug overlays (F1 / F3 / F4 panels): rectangles are laid out in 1080p units and the
    /// GUI matrix scales them to the real screen height, never below 1 so small windows are not shrunk.
    /// No allocations: only structs and floats.
    /// </summary>
    public static class DebugGuiScale
    {
        public const float ReferenceHeight = 1080f;

        /// <summary>Screen-height scale (minimum 1).</summary>
        public static float Scale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        /// <summary>Screen width in scaled GUI units.</summary>
        public static float Width => Screen.width / Scale;

        /// <summary>Screen height in scaled GUI units.</summary>
        public static float Height => Screen.height / Scale;

        /// <summary>Matrix to assign to <see cref="GUI.matrix"/> at the start of OnGUI.</summary>
        public static Matrix4x4 Matrix => Matrix4x4.Scale(Vector3.one * Scale);
    }
}
