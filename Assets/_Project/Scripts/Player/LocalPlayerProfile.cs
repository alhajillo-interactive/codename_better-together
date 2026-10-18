using UnityEngine;

namespace BetterTogether.Player
{
    /// <summary>
    /// The name this machine's player chose, saved between sessions. Each Multiplayer Play Mode clone has its own.
    /// </summary>
    public static class LocalPlayerProfile
    {
        public const int MaxNameLength = 16;

        private const string NameKey = "BetterTogether.PlayerName";

        private static string _name;

        public static string Name
        {
            get => _name ??= Sanitize(PlayerPrefs.GetString(NameKey, ""));
            set
            {
                _name = Sanitize(value);
                PlayerPrefs.SetString(NameKey, _name);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The chosen name, or "Jugador N" when none was set.</summary>
        public static string DisplayNameOrDefault(int playerId)
        {
            var name = Name;
            return string.IsNullOrEmpty(name) ? $"Jugador {playerId}" : name;
        }

        /// <summary>Trims, caps the length and removes rich-text brackets.</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            value = value.Replace("<", "").Replace(">", "").Trim();
            return value.Length > MaxNameLength ? value.Substring(0, MaxNameLength).Trim() : value;
        }
    }
}
