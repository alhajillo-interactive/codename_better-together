using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Local accessibility option "Reducir destellos" (PlayerPrefs <see cref="PrefsKey"/>), read by every full-screen
    /// flash of the camera (the shutter flash of the camper and the "you were photographed" flash of the gnome).
    /// </summary>
    public static class FlashSettings
    {
        public const string PrefsKey = "reduceFlashes";

        /// <summary>True when the player asked for softer flashes. Read it when a flash fires, not every frame.</summary>
        public static bool ReduceFlashes
        {
            get => PlayerPrefs.GetInt(PrefsKey, 0) != 0;
            set => PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
        }
    }
}
