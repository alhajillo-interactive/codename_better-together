// Playtest telemetry front door: gameplay code calls Telemetry.Begin(...).Int(...).Send() at the place where an event
// happens. In release builds this is an empty shell (Enabled is always false), so the hooks cost nothing.
using BetterTogether.Player;
using UnityEngine;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FUSION_WEAVER
using System;
using System.Globalization;
using System.IO;
using System.Text;
using BetterTogether.Environment;
using BetterTogether.Match;
#endif

namespace BetterTogether.Diagnostics
{
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FUSION_WEAVER
    /// <summary>
    /// Writes one CSV row per gameplay event for the match in progress (see <see cref="PlaytestLogger"/>, which owns the
    /// file). Each peer writes only what it sees: state events are hooked on the state authority, input events on the
    /// local player. Hooks must be written as <c>if (Telemetry.Enabled) Telemetry.Begin("name").Int("k", v).Send();</c>:
    /// <see cref="Enabled"/> is false unless a match file is open, and nothing is built or formatted while it is false.
    /// Row format: <c>matchId,t,event,playerId,role,x,z,phase,data</c> where <c>data</c> is <c>key=value;key=value</c>.
    /// </summary>
    public static class Telemetry
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>Header of every telemetry CSV.</summary>
        public const string CsvHeader = "matchId,t,event,playerId,role,x,z,phase,data";

        // Reused for every row, so building a row does not create garbage beyond the final string.
        private static readonly StringBuilder Row = new StringBuilder(256);

        private static StreamWriter _writer;
        private static string _matchId = "";
        private static bool _hasData;
        private static float _t;

        /// <summary>True only while a match file is open. Check it before building any event.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Seconds since the match started (the match timer, so every peer agrees, late joiners included).</summary>
        public static float T
        {
            get
            {
                Tick();
                return _t;
            }
        }

        /// <summary>Day/night source for the <c>phase</c> column; set by the logger when the scene has one.</summary>
        public static DayNightCycle DayNight { get; set; }

        /// <summary>Called by the logger when it opens a match file.</summary>
        internal static void Open(StreamWriter writer, string matchId)
        {
            _writer = writer;
            _matchId = matchId;
            _t = 0f;
            Enabled = writer != null;
        }

        /// <summary>Called by the logger when the match file closes.</summary>
        internal static void Close()
        {
            Enabled = false;
            _writer = null;
        }

        /// <summary>Refreshes the match clock. Frozen once the match is no longer playing, so late events keep the end time.</summary>
        internal static void Tick()
        {
            var match = MatchManager.Instance;
            if (match != null && match.Phase == MatchPhase.Playing)
                _t = Mathf.Max(0f, match.MatchDuration - match.TimeRemaining);
        }

        /// <summary>Starts an event for the local player (position and role of the player on this machine).</summary>
        public static Event Begin(string evt)
        {
            if (!Enabled)
                return default;

            var local = PlayerCharacter.Local;
            return Start(evt, local, local != null ? local.transform : null);
        }

        /// <summary>
        /// Starts an event about <paramref name="actor"/> (any component on the player's root). Used by authority hooks
        /// that run on a machine other than the actor's, so <c>playerId</c>, <c>role</c>, <c>x</c> and <c>z</c> describe the actor.
        /// </summary>
        public static Event Begin(string evt, Component actor)
        {
            if (!Enabled)
                return default;

            if (actor == null || !actor.TryGetComponent(out PlayerCharacter character))
                return Begin(evt);

            return Start(evt, character, character.transform);
        }

        // Telemetry must never break gameplay or network logic: any failure while building a row drops that row.
        private static Event Start(string evt, PlayerCharacter character, Transform at)
        {
            try
            {
                return StartRow(evt, character, at);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] Could not start an event: {e.Message}");
                return default;
            }
        }

        private static Event StartRow(string evt, PlayerCharacter character, Transform at)
        {
            Tick();
            var playerId = -1;
            var role = "none";
            if (character != null && character.Object != null && character.Object.IsValid)
            {
                playerId = character.Object.StateAuthority.PlayerId;
                role = character.Role == PlayerRole.Camper ? "camper" : "gnome";
            }

            var position = at != null ? at.position : Vector3.zero;
            var night = DayNight != null && DayNight.IsNight;

            Row.Clear();
            Row.Append(_matchId).Append(',')
                .Append(_t.ToString("0.00", Invariant)).Append(',')
                .Append(evt).Append(',')
                .Append(playerId).Append(',')
                .Append(role).Append(',')
                .Append(position.x.ToString("0.00", Invariant)).Append(',')
                .Append(position.z.ToString("0.00", Invariant)).Append(',')
                .Append(night ? "night" : "day").Append(',');
            _hasData = false;
            return new Event(true);
        }

        private static StringBuilder Key(string key)
        {
            if (_hasData)
                Row.Append(';');
            _hasData = true;
            return Row.Append(key).Append('=');
        }

        private static void Write()
        {
            if (_writer == null)
                return;

            try
            {
                _writer.WriteLine(Row.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] Could not write an event: {e.Message}");
            }
        }

        /// <summary>Distance (m, horizontal) and angle (degrees) from the camper's facing to <paramref name="from"/>; false without a camper.</summary>
        public static bool TryCamperOffset(Transform from, out float distance, out float angle)
        {
            distance = -1f;
            angle = -1f;
            var all = PlayerCharacter.All;
            for (var i = 0; i < all.Count; i++)
            {
                var player = all[i];
                if (player == null || player.Role != PlayerRole.Camper)
                    continue;

                var toTarget = from.position - player.transform.position;
                toTarget.y = 0f;
                var forward = player.transform.forward;
                forward.y = 0f;
                distance = toTarget.magnitude;
                angle = toTarget.sqrMagnitude > 0.0001f ? Vector3.Angle(forward, toTarget) : 0f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builder for the data column. A struct with no data of its own: when the event was not started (logger off)
        /// every method returns at once, so a disabled chain neither allocates nor formats.
        /// </summary>
        public readonly struct Event
        {
            private readonly bool _on;

            internal Event(bool on) => _on = on;

            public Event Int(string key, int value)
            {
                if (_on)
                    Key(key).Append(value);
                return this;
            }

            public Event Float(string key, float value)
            {
                if (_on)
                    Key(key).Append(value.ToString("0.00", Invariant));
                return this;
            }

            public Event Bool(string key, bool value)
            {
                if (_on)
                    Key(key).Append(value ? '1' : '0');
                return this;
            }

            /// <summary>Text value; separators and line breaks are replaced by '_' so the row stays parseable.</summary>
            public Event Str(string key, string value)
            {
                if (!_on)
                    return this;

                var row = Key(key);
                if (value == null)
                    return this;

                foreach (var c in value)
                    row.Append(c == ',' || c == ';' || c == '=' || c == '\n' || c == '\r' || c == '"' ? '_' : c);
                return this;
            }

            /// <summary>Adds <c>dist_camper</c> and <c>angle_camper</c> measured from <paramref name="from"/> (-1 without a camper).</summary>
            public Event CamperOffset(Transform from)
            {
                if (!_on)
                    return this;

                TryCamperOffset(from, out var distance, out var angle);
                return Float("dist_camper", distance).Float("angle_camper", angle);
            }

            /// <summary>Writes the row.</summary>
            public void Send()
            {
                if (_on)
                    Write();
            }
        }
    }
#else
    /// <summary>Release build: telemetry does not exist. Hooks guarded by <see cref="Enabled"/> compile away entirely.</summary>
    public static class Telemetry
    {
        // A property, not a const: a const would make every guarded hook an "unreachable code" warning (CS0162).
        public static bool Enabled => false;

        public static float T => 0f;

        public static Event Begin(string evt) => default;

        public static Event Begin(string evt, Component actor) => default;

        public static bool TryCamperOffset(Transform from, out float distance, out float angle)
        {
            distance = -1f;
            angle = -1f;
            return false;
        }

        public readonly struct Event
        {
            public Event Int(string key, int value) => this;

            public Event Float(string key, float value) => this;

            public Event Bool(string key, bool value) => this;

            public Event Str(string key, string value) => this;

            public Event CamperOffset(Transform from) => this;

            public void Send()
            {
            }
        }
    }
#endif
}
