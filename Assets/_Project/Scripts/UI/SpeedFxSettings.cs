using UnityEngine;

namespace BetterTogether.UI
{
    /// <summary>
    /// Local option "Efectos de velocidad: sí/no" (PlayerPrefs <see cref="PrefsKey"/>, on by default). When off, the screen-edge
    /// wind lines of <see cref="SpeedLinesHUD"/> are not drawn. Nobody else is affected: the dust at the feet stays.
    /// </summary>
    public static class SpeedFxSettings
    {
        public const string PrefsKey = "speedFx";

        private static int _cached = -1;

        /// <summary>True when the speed effects are wanted. Cached: cheap to read every frame.</summary>
        public static bool Enabled
        {
            get
            {
                if (_cached < 0)
                    _cached = PlayerPrefs.GetInt(PrefsKey, 1) != 0 ? 1 : 0;

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
