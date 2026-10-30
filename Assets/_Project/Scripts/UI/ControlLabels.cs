using System.Text;
using BetterTogether.Player;
using UnityEngine.InputSystem;

namespace BetterTogether.UI
{
    /// <summary>
    /// Which controls each role is taught, and how to print the key of an Input System action.
    /// Keys are read from the action bindings (keyboard and mouse), never typed by hand, so the text stays correct if they change.
    /// </summary>
    public static class ControlLabels
    {
        /// <summary>One taught control: an action of the Input System asset, or a bare key when it is not an action (mute).</summary>
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly string Verb;
            public readonly string ActionPath;
            public readonly Key Key;

            public Entry(string name, string verb, string actionPath, Key key = Key.None)
            {
                Name = name;
                Verb = verb;
                ActionPath = actionPath;
                Key = key;
            }
        }

        private static readonly Entry[] CamperEntries =
        {
            new("interact", "usar", "Player/Interact"),
            new("attack", "patear", "Player/Attack"),
            new("focus", "cámara", "Player/Focus"),
            new("crouch", "agacharte", "Player/Crouch"),
        };

        private static readonly Entry[] GnomeEntries =
        {
            new("interact", "tomar/esconder", "Player/Interact"),
            new("attack", "subirte", "Player/Attack"),
            new("jump", "bajar", "Player/Jump"),
            new("crouch", "agacharte", "Player/Crouch"),
            new("ping", "marcar", "Player/Ping"),
            new("mic", "micro", null, Key.M),
        };

        /// <summary>The longest list of any role (how many lines the HUD needs).</summary>
        public static int MaxEntries => GnomeEntries.Length;

        public static Entry[] For(PlayerRole role) => role == PlayerRole.Camper ? CamperEntries : GnomeEntries;

        /// <summary>The action of an entry, or null when it has none or the asset does not contain it.</summary>
        public static InputAction FindAction(in Entry entry) =>
            string.IsNullOrEmpty(entry.ActionPath) || InputSystem.actions == null ? null : InputSystem.actions.FindAction(entry.ActionPath);

        /// <summary>Text of the key, e.g. "E", "Clic", "Clic der.", "Espacio". Allocates: call it when building text, not per frame.</summary>
        public static string KeyText(in Entry entry)
        {
            if (entry.Key != Key.None)
                return entry.Key.ToString();

            var action = FindAction(entry);
            if (action == null)
                return "?";

            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite)
                    continue;

                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path))
                    continue;

                if (path == "<Mouse>/leftButton")
                    return "Clic";
                if (path == "<Mouse>/rightButton")
                    return "Clic der.";
                if (path == "<Mouse>/middleButton")
                    return "Clic central";
                if (path == "<Keyboard>/space")
                    return "Espacio";
                if (path.StartsWith("<Keyboard>/"))
                    return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            }

            return "?";
        }

        /// <summary>"[E] usar".</summary>
        public static string Format(in Entry entry) => $"[{KeyText(entry)}] {entry.Verb}";

        /// <summary>"[E] usar · [Clic] patear · ..." for a whole role, appended into a reusable builder.</summary>
        public static void AppendAll(StringBuilder builder, PlayerRole role, string separator)
        {
            var entries = For(role);
            for (var i = 0; i < entries.Length; i++)
            {
                if (i > 0)
                    builder.Append(separator);

                builder.Append(Format(entries[i]));
            }
        }
    }
}
