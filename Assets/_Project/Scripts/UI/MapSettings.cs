using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Local option "Mapa: mantener / alternar" (PlayerPrefs <see cref="PrefsKey"/>), read by <see cref="MinimapHUD"/>.
    /// Off (default) = the camp plan is open while the map key is held; on = the key opens and closes it.
    /// </summary>
    public static class MapSettings
    {
        public const string PrefsKey = "mapToggle";

        private static int _cached = -1;

        /// <summary>True when the map key toggles the plan instead of being held. Cached: cheap to read every frame.</summary>
        public static bool Toggle
        {
            get
            {
                if (_cached < 0)
                    _cached = PlayerPrefs.GetInt(PrefsKey, 0) != 0 ? 1 : 0;

                return _cached == 1;
            }
            set
            {
                _cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefsKey, _cached);
            }
        }
    }
}
