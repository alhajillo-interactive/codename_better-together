// C6.2a lint: the UI scripts must not paint hard-coded colours. Text and UI colours come from UiTheme (tokens of the
// semantic palette, Docs/plan/fase-6/plan-ux-ui.md section 3); a hard-coded colour is what made the objective lines
// unreadable on paper. A line that really needs one (a screen flash, a world gizmo) ends with "// ui-color-ok: <reason>".
// Reads the .cs files from disk, so it needs no reference to the game assembly (which an asmdef cannot reference).
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace BetterTogether.Tests
{
    public class UiColorLintTests
    {
        private const string Exemption = "ui-color-ok";
        private const string UiFolder = "_Project/Scripts/UI";

        // The theme itself defines the tokens, so it is the one file allowed to hold literal colours.
        private static readonly string[] ExemptFiles = { "UiTheme.cs" };

        private static readonly Regex HardCoded = new(
            @"new\s+Color(32)?\s*\(|\bColor(32)?\s+\w+\s*=\s*new\s*\(|<color=#|\bColor\.(white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b",
            RegexOptions.Compiled);

        [Test]
        public void UiScripts_HaveNoHardCodedColours()
        {
            var folder = Path.Combine(Application.dataPath, UiFolder);
            Assert.IsTrue(Directory.Exists(folder), $"UI scripts folder not found: {folder}");

            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (System.Array.IndexOf(ExemptFiles, name) >= 0)
                    continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.TrimStart().StartsWith("//") || line.Contains(Exemption))
                        continue;

                    if (HardCoded.IsMatch(line))
                        offenders.Add($"{name}:{i + 1}: {line.Trim()}");
                }
            }

            Assert.IsEmpty(offenders,
                "Hard-coded colours in Scripts/UI (use UiTheme tokens, or add '// ui-color-ok: <reason>' for flashes and gizmos):\n"
                + string.Join("\n", offenders));
        }
    }
}
