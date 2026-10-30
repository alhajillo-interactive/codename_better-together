// Editor-only tool for the UI kit card (C6.1): GnoMore/Aplicar estilo de UI.
//  - Imports the UI sprites with their 9-slice borders, generates Sprites/UI/Knob.png and Pill.png, and creates/wires
//    Resources/UiTheme.asset plus the outline material presets of the theme fonts (never overwriting what a person set).
//  - Gives every element a UiStyleRole (deduced once, then respected) and paints it from the theme: buttons, sliders,
//    inputs, toggles, panels, frames, headers, backdrops and TMP texts. Texts typed by the game at runtime keep their colour.
//  - Targets: MainMenu scene, CampBryan scene (SurveyCanvas only), and the prefabs SettingsPanel, MenuBTN, MatchHUD (only
//    RoleCard, EndScreen, Waiting, Prompt, StoneOverlay, ControlsHint, MicBadge), StartMatch_Canvas.
//  - C8.4: generates the game icons (Sprites/UI/Icons/*.png, 64 px, ink outline) and fills UiTheme.icons.
//  - C6.2a: generates Stamp.png / StampFail.png (ring + check / cross), and on the MatchHUD prefab makes the objectives plate
//    elastic, adds the line stamps and the toast pill, and moves the mic badge into the safe zone (see HudPlateTools).
//  - Idempotent: a value that already matches is not written, so a second run changes nothing. Prefabs are processed
//    before the scenes. Scenes that have unsaved changes are skipped. Scene objects that belong to a prefab instance are
//    left to their prefab. CampIsma and the other people's scenes are never opened.
//  - Report in the Console and in Temp/ui-theme-report.txt.
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class UiThemeApplier
    {
        private const string LogPrefix = "[UiThemeApplier]";
        private const string ReportPath = "Temp/ui-theme-report.txt";

        private const string ThemeFolder = "Assets/_Project/Resources";
        private const string ThemePath = ThemeFolder + "/UiTheme.asset";
        private const string UiSpriteFolder = "Assets/_Project/Sprites/UI";
        private const string PanelSpritePath = UiSpriteFolder + "/tareas-bg.png";
        // Generated: Credits.png has the team names painted in, so it can't be reused as a frame.
        private const string FrameSpritePath = UiSpriteFolder + "/Frame.png";
        private const string CreditsSpritePath = UiSpriteFolder + "/BG/Credits.png";
        private const string ButtonTintSpritePath = UiSpriteFolder + "/ButtonTint.png";
        private const string HeaderSpritePath = UiSpriteFolder + "/BG/InputHeader.png";
        private const string ButtonSpritePath = UiSpriteFolder + "/ButtonBG.png";
        private const string BackdropSpritePath = UiSpriteFolder + "/BG/MainMenuBG.png";
        private const string KnobSpritePath = UiSpriteFolder + "/Knob.png";
        private const string PillSpritePath = UiSpriteFolder + "/Pill.png";
        private const string StampSpritePath = UiSpriteFolder + "/Stamp.png";
        private const string StampFailSpritePath = UiSpriteFolder + "/StampFail.png";
        private const string ViewfinderCornerSpritePath = UiSpriteFolder + "/ViewfinderCorner.png";
        private const string FocusBracketSpritePath = UiSpriteFolder + "/FocusBracket.png";
        private const string PlateSpritePath = UiSpriteFolder + "/Plate.png";
        private const string LensGlintSpritePath = UiSpriteFolder + "/LensGlint.png";
        private const string RingSpritePath = UiSpriteFolder + "/Ring.png";
        private const string FontPath = "Assets/_Project/Fonts/LilitaOne/LilitaOne-Regular SDF.asset";

        private const int DiscPixels = 64;
        private const int DiscBorder = 31;
        private const float DiscRim = 6f;
        private const int UiLayer = 5;

        private const string WaitingOld = "Waiting to start match...";
        private const string WaitingNew = "Esperando a que empiece la partida…";
        private const string ClockOld = "3:00";
        private const string ClockNew = "10:00";

        private const string MainMenuScene = "Assets/_Project/Scenes/MainMenu.unity";
        private const string CampBryanScene = "Assets/_Project/Scenes/CampBryan.unity";

        private static readonly string[] HudParts = { "RoleCard", "EndScreen", "Waiting", "Prompt", "ControlsHint", "MicBadge" };

        // Never restyled (logo art, event system).
        private static readonly HashSet<string> SkipNames = new() { "Logo", "GnomeLogo", "EventSystem" };

        // Texts whose colour the game sets at runtime: only the font is applied.
        private static readonly HashSet<string> RuntimeColorNames = new() { "Status", "StatusText", "State", "Subtitle", "EndScores", "Announcer" };

        private static readonly string[] DangerWords = { "close", "back", "exit", "quit", "leave", "cancel" };
        private static readonly string[] ConfirmWords = { "ready", "create", "join", "send", "enviar", "confirm", "start" };

        private static UiTheme _theme;
        private static int _changes;
        private static readonly Dictionary<string, int> Counts = new();
        private static readonly List<string> Unclassified = new();
        private static readonly List<string> Notes = new();

        // ---------------------------------------------------------------- Entry point

        [MenuItem("GnoMore/Aplicar estilo de UI")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogPrefix} Leave Play Mode first.");
                return;
            }

            _changes = 0;
            Counts.Clear();
            Unclassified.Clear();
            Notes.Clear();

            ImportSprites();
            _theme = EnsureTheme();
            if (_theme == null)
            {
                Debug.LogError($"{LogPrefix} Could not create or load the theme.");
                return;
            }

            ApplyPrefab("Assets/_Project/Prefabs/UI/SettingsPanel.prefab", "Prefab SettingsPanel", null);
            ApplyPrefab("Assets/_Project/Prefabs/UI/MenuBTN.prefab", "Prefab MenuBTN", null);
            ApplyPrefab("Assets/_Project/Prefabs/UI/MatchHUD.prefab", "Prefab MatchHUD", HudParts);
            ApplyPrefab("Assets/_Project/Prefabs/UI/StartMatch_Canvas.prefab", "Prefab StartMatch_Canvas", null);

            ApplyScene(MainMenuScene, "Scene MainMenu", null);
            ApplyScene(CampBryanScene, "Scene CampBryan", "SurveyCanvas");

            AssetDatabase.SaveAssets();
            WriteReport();
        }

        // ---------------------------------------------------------------- Sprites and theme asset

        private static void ImportSprites()
        {
            GenerateDisc(KnobSpritePath, true);
            GenerateDisc(PillSpritePath, false);
            GenerateStamp(StampSpritePath, false);
            GenerateStamp(StampFailSpritePath, true);
            GenerateFrame(FrameSpritePath);
            GenerateNeutralButton(ButtonSpritePath, ButtonTintSpritePath);
            GenerateViewfinderCorner(ViewfinderCornerSpritePath);
            GenerateFocusBracket(FocusBracketSpritePath);
            GeneratePlate(PlateSpritePath);
            GenerateLensGlint(LensGlintSpritePath);
            GenerateRing(RingSpritePath);
            GenerateIcons();

            ConfigureSprite(ButtonSpritePath, 24);
            ConfigureSprite(HeaderSpritePath, 28);
            ConfigureSprite(FrameSpritePath, FrameBorder);
            ConfigureSprite(ButtonTintSpritePath, 24);
            ConfigureSprite(KnobSpritePath, DiscBorder);
            ConfigureSprite(PillSpritePath, DiscBorder);
            ConfigureSprite(StampSpritePath, 0);
            ConfigureSprite(StampFailSpritePath, 0);
            ConfigureSprite(ViewfinderCornerSpritePath, 0);
            ConfigureSprite(FocusBracketSpritePath, 0);
            ConfigureSprite(PlateSpritePath, 0);
            ConfigureSprite(LensGlintSpritePath, 0);
            ConfigureSprite(RingSpritePath, 0);
            for (var i = 1; i <= (int)UiIcon.MicOff; i++)
                ConfigureSprite(IconPath((UiIcon)i), 0);
        }

        // ---- C8.2 viewfinder sprites (128 px, written once, never overwritten)

        private const int ViewfinderPixels = 128;
        private const int CornerArm = 124;
        private const int CornerThickness = 30;
        private const float CornerOutline = 4f;
        private const int BracketArm = 64;
        private const int BracketThickness = 10;

        // Depth inside the union of the two arms of a top-left L (positive inside, in pixels; origin bottom-left).
        private static float LDepth(float x, float y, int arm, int thickness)
        {
            const float top = ViewfinderPixels;
            var horizontal = Mathf.Min(Mathf.Min(x, arm - x), Mathf.Min(y - (top - thickness), top - y));
            var vertical = Mathf.Min(Mathf.Min(x, thickness - x), Mathf.Min(y - (top - arm), top - y));
            return Mathf.Max(horizontal, vertical);
        }

        // Wooden L with an ink outline and grain stripes along each arm: the corner of the viewfinder.
        private static void GenerateViewfinderCorner(string path)
        {
            if (File.Exists(path))
                return;

            var defaults = ScriptableObject.CreateInstance<UiTheme>();
            Color ink = defaults.ink, wood = defaults.wood, grain = defaults.woodDark;
            Object.DestroyImmediate(defaults);

            var pixels = new Color32[ViewfinderPixels * ViewfinderPixels];
            for (var y = 0; y < ViewfinderPixels; y++)
            {
                for (var x = 0; x < ViewfinderPixels; x++)
                {
                    var depth = LDepth(x + 0.5f, y + 0.5f, CornerArm, CornerThickness);
                    var horizontalArm = y + 0.5f >= ViewfinderPixels - CornerThickness;
                    var along = horizontalArm ? y : x;
                    var stripe = Mathf.Sin(along * 0.85f + Mathf.Sin((horizontalArm ? x : y) * 0.12f) * 2f);
                    var fill = Color.Lerp(wood, grain, Mathf.Clamp01(stripe - 0.55f) * 0.9f);
                    var color = depth < CornerOutline ? ink : fill;
                    color.a = Mathf.Clamp01(depth + 0.5f);
                    pixels[y * ViewfinderPixels + x] = color;
                }
            }

            WritePng(path, ViewfinderPixels, ViewfinderPixels, pixels);
        }

        // Thin white L (tinted at runtime): the focus bracket around a locked gnome.
        private static void GenerateFocusBracket(string path)
        {
            if (File.Exists(path))
                return;

            var pixels = new Color32[ViewfinderPixels * ViewfinderPixels];
            for (var y = 0; y < ViewfinderPixels; y++)
            {
                for (var x = 0; x < ViewfinderPixels; x++)
                {
                    var depth = LDepth(x + 0.5f, y + 0.5f, BracketArm, BracketThickness);
                    var color = (Color32)Color.white;
                    color.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(depth + 0.5f) * 255f);
                    pixels[y * ViewfinderPixels + x] = color;
                }
            }

            WritePng(path, ViewfinderPixels, ViewfinderPixels, pixels);
        }

        // Sepia glass plate: rounded square, ink outline and a diagonal shine.
        private static void GeneratePlate(string path)
        {
            if (File.Exists(path))
                return;

            var defaults = ScriptableObject.CreateInstance<UiTheme>();
            Color ink = defaults.ink, sepia = defaults.photoSepia;
            Object.DestroyImmediate(defaults);

            const float radius = 22f;
            const float outline = 5f;
            var half = ViewfinderPixels * 0.5f;
            var pixels = new Color32[ViewfinderPixels * ViewfinderPixels];
            for (var y = 0; y < ViewfinderPixels; y++)
            {
                for (var x = 0; x < ViewfinderPixels; x++)
                {
                    var px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    var py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    var distance = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    var depth = -distance;

                    // Shine: a soft diagonal band, stronger on the upper-left half.
                    var band = Mathf.Abs(x - y + 20f) / 14f;
                    var shine = Mathf.Clamp01(1f - band) * (x + y < ViewfinderPixels ? 0.55f : 0.15f);
                    var color = depth < outline ? ink : Color.Lerp(sepia, Color.white, shine);
                    color.a = Mathf.Clamp01(depth + 0.5f);
                    pixels[y * ViewfinderPixels + x] = color;
                }
            }

            WritePng(path, ViewfinderPixels, ViewfinderPixels, pixels);
        }

        // White four-pointed star (astroid): the lens glint, tinted at runtime.
        private static void GenerateLensGlint(string path)
        {
            if (File.Exists(path))
                return;

            var half = ViewfinderPixels * 0.5f;
            var pixels = new Color32[ViewfinderPixels * ViewfinderPixels];
            for (var y = 0; y < ViewfinderPixels; y++)
            {
                for (var x = 0; x < ViewfinderPixels; x++)
                {
                    var ax = Mathf.Abs(x + 0.5f - half) / half;
                    var ay = Mathf.Abs(y + 0.5f - half) / half;
                    var inside = 1f - (Mathf.Sqrt(ax) + Mathf.Sqrt(ay));
                    var color = (Color32)Color.white;
                    color.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(inside * 4f) * 255f);
                    pixels[y * ViewfinderPixels + x] = color;
                }
            }

            WritePng(path, ViewfinderPixels, ViewfinderPixels, pixels);
        }

        // White ring for radial progress (Filled Radial 360, tinted at runtime).
        private static void GenerateRing(string path)
        {
            if (File.Exists(path))
                return;

            const float thickness = 14f;
            var center = (ViewfinderPixels - 1) * 0.5f;
            var radius = ViewfinderPixels * 0.5f;
            var pixels = new Color32[ViewfinderPixels * ViewfinderPixels];
            for (var y = 0; y < ViewfinderPixels; y++)
            {
                for (var x = 0; x < ViewfinderPixels; x++)
                {
                    var distance = new Vector2(x - center, y - center).magnitude;
                    var coverage = Mathf.Clamp01(radius - distance) * Mathf.Clamp01(distance - (radius - thickness));
                    var color = (Color32)Color.white;
                    color.a = (byte)Mathf.RoundToInt(coverage * 255f);
                    pixels[y * ViewfinderPixels + x] = color;
                }
            }

            WritePng(path, ViewfinderPixels, ViewfinderPixels, pixels);
        }

        private const int StampPixels = 64;
        private const float StampRing = 5f;
        private const float StampGlyphHalfWidth = 3f;

        // Writes a 64 px ink stamp: a ring with a check mark (ink) or a cross (hatDark). Never overwrites an existing file.
        private static void GenerateStamp(string path, bool cross)
        {
            if (File.Exists(path))
                return;

            var defaults = ScriptableObject.CreateInstance<UiTheme>();
            var ink = (Color32)(cross ? defaults.hatDark : defaults.ink);
            Object.DestroyImmediate(defaults);

            // Glyph segments in pixel coordinates (origin bottom-left).
            var segments = cross
                ? new[] { (new Vector2(21f, 21f), new Vector2(43f, 43f)), (new Vector2(21f, 43f), new Vector2(43f, 21f)) }
                : new[] { (new Vector2(18f, 32f), new Vector2(28f, 21f)), (new Vector2(28f, 21f), new Vector2(47f, 43f)) };

            var pixels = new Color32[StampPixels * StampPixels];
            var center = (StampPixels - 1) * 0.5f;
            var radius = StampPixels * 0.5f;
            for (var y = 0; y < StampPixels; y++)
            {
                for (var x = 0; x < StampPixels; x++)
                {
                    var point = new Vector2(x, y);
                    var distance = (point - new Vector2(center, center)).magnitude;
                    var coverage = Mathf.Clamp01(radius - distance) * Mathf.Clamp01(distance - (radius - StampRing));
                    foreach (var (a, b) in segments)
                        coverage = Mathf.Max(coverage, Mathf.Clamp01(StampGlyphHalfWidth + 0.5f - SegmentDistance(point, a, b)));

                    var color = ink;
                    color.a = (byte)Mathf.RoundToInt(coverage * 255f);
                    pixels[y * StampPixels + x] = color;
                }
            }

            WritePng(path, StampPixels, StampPixels, pixels);
        }

        private static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return (point - (a + ab * t)).magnitude;
        }

        // ---- C8.4 game icons (64 px, ink outline, flat theme colours; written once, never overwritten)

        private const string IconFolder = UiSpriteFolder + "/Icons";
        private const int IconPixels = 64;
        private const float IconOutline = 3.5f;

        private static string IconPath(UiIcon icon) => $"{IconFolder}/{icon}.png";

        private readonly struct IconShape
        {
            public readonly System.Func<Vector2, float> Distance;
            public readonly Color32 Fill;
            public readonly bool Outlined;

            public IconShape(System.Func<Vector2, float> distance, Color32 fill, bool outlined)
            {
                Distance = distance;
                Fill = fill;
                Outlined = outlined;
            }
        }

        private static void GenerateIcons()
        {
            if (!AssetDatabase.IsValidFolder(IconFolder))
                AssetDatabase.CreateFolder(UiSpriteFolder, "Icons");

            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                for (var i = 1; i <= (int)UiIcon.MicOff; i++)
                {
                    var icon = (UiIcon)i;
                    var path = IconPath(icon);
                    if (File.Exists(path))
                        continue;

                    WritePng(path, IconPixels, IconPixels, RenderIcon(BuildIcon(icon, theme), theme.ink));
                }
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // Paints the shapes in order, each one over a 3.5 px ink halo (when outlined) and then its flat fill; coverage is analytic.
        private static Color32[] RenderIcon(List<IconShape> shapes, Color32 ink)
        {
            var pixels = new Color32[IconPixels * IconPixels];
            for (var y = 0; y < IconPixels; y++)
            {
                for (var x = 0; x < IconPixels; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    float r = 0f, g = 0f, b = 0f, a = 0f; // premultiplied
                    foreach (var shape in shapes)
                    {
                        var distance = shape.Distance(point);
                        if (shape.Outlined)
                            Over(ref r, ref g, ref b, ref a, ink, Mathf.Clamp01(0.5f - (distance - IconOutline)));
                        Over(ref r, ref g, ref b, ref a, shape.Fill, Mathf.Clamp01(0.5f - distance));
                    }

                    pixels[y * IconPixels + x] = a <= 0f
                        ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)Mathf.RoundToInt(r / a * 255f), (byte)Mathf.RoundToInt(g / a * 255f),
                            (byte)Mathf.RoundToInt(b / a * 255f), (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            return pixels;
        }

        private static void Over(ref float r, ref float g, ref float b, ref float a, Color32 color, float coverage)
        {
            if (coverage <= 0f)
                return;

            var keep = 1f - coverage;
            r = color.r / 255f * coverage + r * keep;
            g = color.g / 255f * coverage + g * keep;
            b = color.b / 255f * coverage + b * keep;
            a = coverage + a * keep;
        }

        private static void Add(List<IconShape> shapes, System.Func<Vector2, float> distance, Color fill, bool outlined = true)
            => shapes.Add(new IconShape(distance, fill, outlined));

        private static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        // ---- Signed distance primitives (pixel space, origin bottom-left; negative inside)

        private static System.Func<Vector2, float> Circle(Vector2 center, float radius)
            => p => (p - center).magnitude - radius;

        private static System.Func<Vector2, float> Capsule(Vector2 a, Vector2 b, float radius)
            => p => SegmentDistance(p, a, b) - radius;

        private static System.Func<Vector2, float> RoundBox(Vector2 center, Vector2 half, float radius)
            => p =>
            {
                var d = new Vector2(Mathf.Abs(p.x - center.x) - half.x + radius, Mathf.Abs(p.y - center.y) - half.y + radius);
                return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude - radius;
            };

        // Polygon grown by "round" pixels (softens the corners).
        private static System.Func<Vector2, float> Polygon(float round, params Vector2[] v)
            => p =>
            {
                var d = Vector2.Dot(p - v[0], p - v[0]);
                var sign = 1f;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
                {
                    var e = v[j] - v[i];
                    var w = p - v[i];
                    var q = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                    d = Mathf.Min(d, Vector2.Dot(q, q));
                    var c1 = p.y >= v[i].y;
                    var c2 = p.y < v[j].y;
                    var c3 = e.x * w.y > e.y * w.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                        sign = -sign;
                }

                return sign * Mathf.Sqrt(d) - round;
            };

        private static System.Func<Vector2, float> Union(System.Func<Vector2, float> a, System.Func<Vector2, float> b)
            => p => Mathf.Min(a(p), b(p));

        private static System.Func<Vector2, float> Intersect(System.Func<Vector2, float> a, System.Func<Vector2, float> b)
            => p => Mathf.Max(a(p), b(p));

        private static List<IconShape> BuildIcon(UiIcon icon, UiTheme t)
        {
            var s = new List<IconShape>();
            switch (icon)
            {
                case UiIcon.Log:
                    var logBody = Capsule(new Vector2(17f, 32f), new Vector2(42f, 32f), 11f);
                    Add(s, logBody, t.wood);
                    Add(s, Intersect(logBody, Capsule(new Vector2(14f, 38f), new Vector2(44f, 38f), 1.5f)), t.woodDark, false);
                    Add(s, Intersect(logBody, Capsule(new Vector2(14f, 26f), new Vector2(44f, 26f), 1.5f)), t.woodDark, false);
                    Add(s, Circle(new Vector2(44f, 32f), 11f), t.woodLight);
                    Add(s, Circle(new Vector2(44f, 32f), 6f), t.wood, false);
                    Add(s, Circle(new Vector2(44f, 32f), 2.2f), t.woodDark, false);
                    break;
                case UiIcon.Match:
                    Add(s, Capsule(new Vector2(17f, 17f), new Vector2(43f, 43f), 4.5f), t.woodLight);
                    Add(s, Circle(new Vector2(47f, 47f), 9f), t.hatDark);
                    Add(s, Circle(new Vector2(50f, 50f), 3.5f), t.ember, false);
                    break;
                case UiIcon.SausageRaw:
                case UiIcon.SausageCooked:
                case UiIcon.SausageBurnt:
                    BuildSausage(s, t, icon);
                    break;
                case UiIcon.Fire:
                    Add(s, Union(Circle(new Vector2(32f, 27f), 13f), Polygon(1f, new Vector2(20f, 31f), new Vector2(44f, 31f), new Vector2(33f, 59f))), t.hatDark);
                    Add(s, Union(Circle(new Vector2(32f, 25f), 7.5f), Polygon(1f, new Vector2(25.5f, 28f), new Vector2(38.5f, 28f), new Vector2(32f, 45f))), t.ember, false);
                    Add(s, Capsule(new Vector2(14f, 14f), new Vector2(50f, 22f), 5f), t.wood);
                    Add(s, Capsule(new Vector2(14f, 22f), new Vector2(50f, 14f), 5f), t.woodDark);
                    break;
                case UiIcon.Bucket:
                    var handle = new Vector2(32f, 44f);
                    Add(s, p => Mathf.Max(Mathf.Abs((p - handle).magnitude - 12.5f) - 2f, handle.y - p.y), t.woodDark);
                    var bucketBody = Polygon(2f, new Vector2(15f, 46f), new Vector2(49f, 46f), new Vector2(44f, 10f), new Vector2(20f, 10f));
                    Add(s, bucketBody, t.roleGnome);
                    Add(s, Intersect(bucketBody, RoundBox(new Vector2(32f, 34f), new Vector2(30f, 3.5f), 0f)), t.woodLight, false);
                    Add(s, Intersect(bucketBody, RoundBox(new Vector2(32f, 19f), new Vector2(30f, 3.5f), 0f)), t.woodLight, false);
                    Add(s, RoundBox(new Vector2(32f, 46f), new Vector2(18.5f, 3.5f), 2f), t.woodLight);
                    break;
                case UiIcon.Cage:
                    Add(s, RoundBox(new Vector2(32f, 31f), new Vector2(20f, 17f), 4f), t.woodDark);
                    Add(s, Capsule(new Vector2(22f, 16f), new Vector2(22f, 46f), 3f), t.woodLight);
                    Add(s, Capsule(new Vector2(32f, 16f), new Vector2(32f, 46f), 3f), t.woodLight);
                    Add(s, Capsule(new Vector2(42f, 16f), new Vector2(42f, 46f), 3f), t.woodLight);
                    Add(s, RoundBox(new Vector2(32f, 50f), new Vector2(24f, 3.5f), 2f), t.woodLight);
                    Add(s, RoundBox(new Vector2(32f, 12f), new Vector2(24f, 3.5f), 2f), t.woodLight);
                    break;
                case UiIcon.Kick:
                    var foot = RoundBox(new Vector2(35f, 19f), new Vector2(20f, 9f), 6f);
                    Add(s, Union(RoundBox(new Vector2(26f, 40f), new Vector2(9f, 15f), 3f), foot), t.wood);
                    Add(s, Intersect(foot, RoundBox(new Vector2(35f, 12.5f), new Vector2(20f, 3.2f), 0f)), t.woodDark, false);
                    Add(s, RoundBox(new Vector2(26f, 53f), new Vector2(11f, 3.5f), 2f), t.cream);
                    break;
                case UiIcon.Gnome:
                    Add(s, Polygon(3f, new Vector2(18f, 35f), new Vector2(46f, 35f), new Vector2(32f, 10f)), t.cream);
                    Add(s, Circle(new Vector2(32f, 35f), 9f), t.woodLight);
                    Add(s, Circle(new Vector2(32f, 32f), 3.5f), t.wood, false);
                    Add(s, Circle(new Vector2(27.5f, 36f), 1.6f), t.ink, false);
                    Add(s, Circle(new Vector2(36.5f, 36f), 1.6f), t.ink, false);
                    Add(s, Union(Polygon(1.5f, new Vector2(20f, 41f), new Vector2(44f, 41f), new Vector2(38f, 56f)),
                        Capsule(new Vector2(18f, 41f), new Vector2(46f, 41f), 3f)), t.hatDark);
                    break;
                case UiIcon.Hide:
                    Add(s, Capsule(new Vector2(9f, 14f), new Vector2(55f, 14f), 3f), t.woodDark);
                    Add(s, Union(Union(Circle(new Vector2(21f, 27f), 11f), Circle(new Vector2(43f, 27f), 11f)),
                        Union(Circle(new Vector2(32f, 36f), 14f), Circle(new Vector2(32f, 24f), 12f))), t.leafDark);
                    Add(s, Circle(new Vector2(26f, 42f), 4f), t.leafLight, false);
                    Add(s, Circle(new Vector2(44f, 34f), 2.5f), t.leafLight, false);
                    Add(s, Circle(new Vector2(26f, 28f), 4.4f), t.cream);
                    Add(s, Circle(new Vector2(38f, 28f), 4.4f), t.cream);
                    Add(s, Circle(new Vector2(26.6f, 27.4f), 1.9f), t.ink, false);
                    Add(s, Circle(new Vector2(38.6f, 27.4f), 1.9f), t.ink, false);
                    break;
                case UiIcon.MicOn:
                case UiIcon.MicOff:
                    var mic = new Vector2(32f, 38f);
                    Add(s, p => Mathf.Max(Mathf.Abs((p - mic).magnitude - 14f) - 2.2f, p.y - mic.y), t.cream);
                    Add(s, Capsule(new Vector2(32f, 24f), new Vector2(32f, 16f), 2.2f), t.cream);
                    Add(s, Capsule(new Vector2(22f, 14f), new Vector2(42f, 14f), 2.5f), t.cream);
                    Add(s, Capsule(new Vector2(32f, 38f), new Vector2(32f, 45f), 8.5f), icon == UiIcon.MicOn ? t.leafLight : t.cream);
                    if (icon == UiIcon.MicOff)
                        Add(s, Capsule(new Vector2(13f, 53f), new Vector2(51f, 11f), 3.5f), t.hatDark);
                    break;
            }

            return s;
        }

        // Diagonal sausage: raw is pink, cooked is browned with grill marks, burnt is dark with black marks.
        private static void BuildSausage(List<IconShape> s, UiTheme t, UiIcon icon)
        {
            var a = new Vector2(18f, 22f);
            var b = new Vector2(46f, 42f);
            var body = Capsule(a, b, 10.5f);
            var fill = icon == UiIcon.SausageRaw ? Mix(t.hatDark, t.cream, 0.55f) : icon == UiIcon.SausageCooked ? t.wood : t.inkSoft;
            var shine = icon == UiIcon.SausageRaw ? t.cream : icon == UiIcon.SausageCooked ? t.woodLight : t.wood;
            Add(s, body, fill);
            Add(s, Capsule(new Vector2(18.4f, 28.4f), new Vector2(39.9f, 43.7f), 2.2f), shine, false);

            if (icon == UiIcon.SausageRaw)
                return;

            var axis = (b - a).normalized;
            var across = new Vector2(-axis.y, axis.x) * 13f;
            var mark = icon == UiIcon.SausageCooked ? t.woodDark : t.ink;
            foreach (var along in new[] { 0.32f, 0.52f, 0.72f })
            {
                var center = a + (b - a) * along;
                Add(s, Intersect(body, Capsule(center - across, center + across, 1.8f)), mark, false);
            }
        }

        private const int FramePixels = 128;
        private const int FrameBorder = 44;
        private const float FrameRadius = 30f;
        private const float FrameWood = 14f;
        private const float FrameRim = 4f;

        // Rounded wooden frame with a paper inside (the look of the credits board, without its names): dark rim, wood, paper.
        private static void GenerateFrame(string path)
        {
            if (File.Exists(path))
                return;

            var defaults = ScriptableObject.CreateInstance<UiTheme>();
            Color rim = defaults.woodDark, wood = defaults.wood, paper = defaults.paper;
            Object.DestroyImmediate(defaults);

            var pixels = new Color32[FramePixels * FramePixels];
            var half = FramePixels * 0.5f;
            for (var y = 0; y < FramePixels; y++)
            {
                for (var x = 0; x < FramePixels; x++)
                {
                    // Signed distance to the rounded rectangle (negative inside).
                    var px = Mathf.Abs(x + 0.5f - half) - (half - FrameRadius);
                    var py = Mathf.Abs(y + 0.5f - half) - (half - FrameRadius);
                    var outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - FrameRadius;
                    var depth = -outside;
                    var color = depth < FrameRim ? rim : depth < FrameWood ? wood : paper;
                    if (depth >= FrameRim && depth < FrameRim + 2f)
                        color = Color.Lerp(rim, wood, (depth - FrameRim) * 0.5f);
                    if (depth >= FrameWood && depth < FrameWood + 2f)
                        color = Color.Lerp(rim, paper, (depth - FrameWood) * 0.5f);
                    color.a = Mathf.Clamp01(depth + 0.5f);
                    pixels[y * FramePixels + x] = color;
                }
            }

            WritePng(path, FramePixels, FramePixels, pixels);
        }

        // Greyscale copy of the wood button, brightened so its lightest fill is white: a Selectable tint then gives clean colours.
        private static void GenerateNeutralButton(string source, string path)
        {
            if (File.Exists(path) || !File.Exists(source))
                return;

            var input = new Texture2D(2, 2);
            input.LoadImage(File.ReadAllBytes(source));
            var pixels = input.GetPixels32();
            var maxLuma = 0.01f;
            foreach (var p in pixels)
            {
                if (p.a > 200)
                    maxLuma = Mathf.Max(maxLuma, Luma(p));
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                var v = (byte)Mathf.RoundToInt(Mathf.Clamp01(Luma(pixels[i]) / maxLuma) * 255f);
                pixels[i] = new Color32(v, v, v, pixels[i].a);
            }

            var width = input.width;
            var height = input.height;
            Object.DestroyImmediate(input);
            WritePng(path, width, height, pixels);
        }

        private static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private static void WritePng(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            _changes++;
            Notes.Add($"Generated {path}");
        }

        // Writes a 64 px anti-aliased disc: paper with a woodDark rim (knob) or plain white (pill, to be tinted).
        private static void GenerateDisc(string path, bool paperWithRim)
        {
            if (File.Exists(path))
                return;

            var defaults = ScriptableObject.CreateInstance<UiTheme>();
            var paper = (Color32)defaults.paper;
            var rim = (Color32)defaults.woodDark;
            Object.DestroyImmediate(defaults);

            var pixels = new Color32[DiscPixels * DiscPixels];
            var center = (DiscPixels - 1) * 0.5f;
            var radius = DiscPixels * 0.5f;
            for (var y = 0; y < DiscPixels; y++)
            {
                for (var x = 0; x < DiscPixels; x++)
                {
                    var distance = new Vector2(x - center, y - center).magnitude;
                    var coverage = Mathf.Clamp01(radius - distance);
                    Color32 color = Color.white;
                    if (paperWithRim)
                        color = Color32.Lerp(paper, rim, Mathf.Clamp01(distance - (radius - DiscRim)));
                    color.a = (byte)Mathf.RoundToInt(coverage * 255f);
                    pixels[y * DiscPixels + x] = color;
                }
            }

            var texture = new Texture2D(DiscPixels, DiscPixels, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            _changes++;
            Notes.Add($"Generated {path}");
        }

        // Makes the texture a single sprite with a uniform 9-slice border. Writes nothing when it already matches.
        private static void ConfigureSprite(string path, int border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Notes.Add($"Missing sprite: {path}");
                return;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            var wanted = new Vector4(border, border, border, border);
            var correct = importer.textureType == TextureImporterType.Sprite
                          && importer.spriteImportMode == SpriteImportMode.Single
                          && importer.spriteBorder == wanted
                          && importer.alphaIsTransparency
                          && !importer.mipmapEnabled
                          && settings.spriteMeshType == SpriteMeshType.FullRect;
            if (correct)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = wanted;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            _changes++;
            Notes.Add($"Sprite border {border}: {path}");
        }

        // Creates the theme asset when missing and fills only the references that are still empty.
        private static UiTheme EnsureTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            var dirty = false;
            if (theme == null)
            {
                if (!AssetDatabase.IsValidFolder(ThemeFolder))
                    AssetDatabase.CreateFolder("Assets/_Project", "Resources");
                theme = ScriptableObject.CreateInstance<UiTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
                dirty = true;
                Notes.Add($"Created {ThemePath}");
            }

            Fill(ref theme.panel, PanelSpritePath, ref dirty);
            if (theme.frame != null && AssetDatabase.GetAssetPath(theme.frame) == CreditsSpritePath)
                theme.frame = null;
            Fill(ref theme.frame, FrameSpritePath, ref dirty);
            Fill(ref theme.buttonTint, ButtonTintSpritePath, ref dirty);
            Fill(ref theme.header, HeaderSpritePath, ref dirty);
            Fill(ref theme.button, ButtonSpritePath, ref dirty);
            Fill(ref theme.knob, KnobSpritePath, ref dirty);
            Fill(ref theme.pill, PillSpritePath, ref dirty);
            Fill(ref theme.stamp, StampSpritePath, ref dirty);
            Fill(ref theme.stampFail, StampFailSpritePath, ref dirty);
            Fill(ref theme.viewfinderCorner, ViewfinderCornerSpritePath, ref dirty);
            Fill(ref theme.focusBracket, FocusBracketSpritePath, ref dirty);
            Fill(ref theme.plate, PlateSpritePath, ref dirty);
            Fill(ref theme.lensGlint, LensGlintSpritePath, ref dirty);
            Fill(ref theme.ring, RingSpritePath, ref dirty);
            Fill(ref theme.backdrop, BackdropSpritePath, ref dirty);

            var iconCount = (int)UiIcon.MicOff + 1;
            if (theme.icons == null || theme.icons.Length != iconCount)
            {
                System.Array.Resize(ref theme.icons, iconCount);
                dirty = true;
            }

            for (var i = 1; i < iconCount; i++)
                Fill(ref theme.icons[i], IconPath((UiIcon)i), ref dirty);

            if (theme.titleFont == null)
            {
                theme.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                dirty = true;
            }

            if (theme.bodyFont == null)
            {
                theme.bodyFont = theme.titleFont;
                dirty = true;
            }

            if (theme.titleFont != null && theme.titleOutline == null)
            {
                theme.titleOutline = EnsureOutline(theme, theme.titleFont);
                dirty = true;
            }

            if (theme.bodyFont != null && theme.bodyOutline == null)
            {
                theme.bodyOutline = EnsureOutline(theme, theme.bodyFont);
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(theme);
                _changes++;
            }

            return theme;
        }

        private static void Fill(ref Sprite field, string path, ref bool dirty)
        {
            if (field != null)
                return;

            field = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (field == null)
                Notes.Add($"Sprite not found for the theme: {path}");
            else
                dirty = true;
        }

        // Material preset "<font> Outline.mat" next to the font asset: the font material plus an ink outline.
        private static Material EnsureOutline(UiTheme theme, TMP_FontAsset font)
        {
            var fontPath = AssetDatabase.GetAssetPath(font);
            var folder = Path.GetDirectoryName(fontPath)?.Replace('\\', '/');
            var materialPath = $"{folder}/{Path.GetFileNameWithoutExtension(fontPath)} Outline.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
                return material;

            ShaderUtilities.GetShaderPropertyIDs();
            material = new Material(font.material);
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, theme.ink);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, theme.outlineWidth);
            AssetDatabase.CreateAsset(material, materialPath);
            Notes.Add($"Created {materialPath}");
            return material;
        }

        // ---------------------------------------------------------------- Targets

        private static void ApplyPrefab(string path, string target, string[] onlyParts)
        {
            if (!File.Exists(path))
            {
                Notes.Add($"Prefab not found: {path}");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var before = _changes;
                StyleRoots(root.transform, target, onlyParts, false);
                if (onlyParts != null)
                    _changes += HudPlateTools.Ensure(root, _theme);
                if (_changes != before)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // onlyObject: when set, only objects with that name are styled (SurveyCanvas inside CampBryan).
        private static void ApplyScene(string path, string target, string onlyObject)
        {
            if (!File.Exists(path))
            {
                Notes.Add($"Scene not found: {path}");
                return;
            }

            var scene = SceneManager.GetSceneByPath(path);
            var openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            // Saving below would also save the user's own pending edits in an already open scene, so skip it.
            if (!openedHere && scene.isDirty)
            {
                Debug.LogWarning($"{LogPrefix} {path} has unsaved changes; save it and run again.");
                Notes.Add($"Skipped (unsaved changes): {path}");
                return;
            }

            var before = _changes;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (onlyObject != null)
                {
                    var found = FindDeep(go.transform, onlyObject);
                    if (found != null)
                        StyleRoots(found, target, null, true);
                }
                else if (go.GetComponentInChildren<Canvas>(true) != null)
                {
                    StyleRoots(go.transform, target, null, true);
                }
            }

            if (_changes != before)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }

        private static void StyleRoots(Transform top, string target, string[] onlyParts, bool isScene)
        {
            if (onlyParts == null)
            {
                Style(top, target, isScene);
                EnsureModals(top, target);
                return;
            }

            foreach (var part in onlyParts)
            {
                var found = FindDeep(top, part);
                if (found != null)
                    Style(found, target, isScene);
                else
                    Notes.Add($"{target}: part '{part}' not found");
            }

            // The modal components (RoleCard, MatchHUD) sit on the prefab root, not inside the styled parts.
            EnsureModals(top, target);

            // Initial texts that show for a moment before the game updates them.
            foreach (var text in top.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "TimeLabel" && text.text == ClockOld)
                    SetText(text, ClockNew);
            }
        }

        // ---------------------------------------------------------------- Styling pass

        private static void Style(Transform top, string target, bool isScene)
        {
            var transforms = top.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (IsSkipped(t, top, isScene))
                    continue;

                var go = t.gameObject;
                if (go.TryGetComponent<TMP_Text>(out var text))
                {
                    StyleText(t, text, target);
                    continue;
                }

                if (go.TryGetComponent<Slider>(out var slider))
                    StyleSlider(slider, target);
                else if (go.TryGetComponent<TMP_InputField>(out var input))
                    StyleInput(input, target);
                else if (go.TryGetComponent<Toggle>(out var toggle))
                    StyleToggle(toggle, target);
                else if (go.TryGetComponent<Button>(out var button))
                    StyleButton(button, target);
                else if (go.TryGetComponent<Image>(out var image) && t.GetComponentInParent<Selectable>(true) == null)
                    StyleImage(t, image, target);
            }

            EnsureLobbyBackground(top, target);
        }

        private static bool IsSkipped(Transform t, Transform top, bool isScene)
        {
            if (isScene && PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
                return true;

            for (var p = t; p != null; p = p.parent)
            {
                if (SkipNames.Contains(p.name))
                    return true;
                if (p.TryGetComponent<UiStyleRole>(out var role) && role.Role == UiRole.Skip)
                    return true;
                if (p == top)
                    break;
            }

            return false;
        }

        // ---- Buttons

        private static void StyleButton(Button button, string target)
        {
            var role = EnsureRole(button.gameObject, DeduceButtonRole(button.name));
            if (role == UiRole.Skip)
                return;

            Count(target, role);
            var tint = role == UiRole.ButtonConfirm ? _theme.leafDark : role == UiRole.ButtonDanger ? _theme.hatDark : Color.white;
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                SetSprite(image, role != UiRole.Button && _theme.buttonTint != null ? _theme.buttonTint : _theme.button);
                SetColor(image, Color.white);
                if (button.targetGraphic != image)
                {
                    button.targetGraphic = image;
                    Dirty(button);
                }
            }

            if (button.transition != Selectable.Transition.ColorTint)
            {
                button.transition = Selectable.Transition.ColorTint;
                Dirty(button);
            }

            SetColors(button, TintBlock(tint));
            if (!button.TryGetComponent<UiButtonFeel>(out _))
            {
                button.gameObject.AddComponent<UiButtonFeel>();
                _changes++;
            }
        }

        private static UiRole DeduceButtonRole(string name)
        {
            var lower = name.ToLowerInvariant();
            foreach (var word in DangerWords)
            {
                if (lower.Contains(word))
                    return UiRole.ButtonDanger;
            }

            foreach (var word in ConfirmWords)
            {
                if (lower.Contains(word))
                    return UiRole.ButtonConfirm;
            }

            return UiRole.Button;
        }

        // Normal = tint, highlighted = 1.08 brighter, pressed = 0.85, disabled = grey at 50 %.
        private static ColorBlock TintBlock(Color tint)
        {
            return new ColorBlock
            {
                normalColor = tint,
                highlightedColor = Scaled(tint, 1.08f),
                pressedColor = Scaled(tint, 0.85f),
                selectedColor = Scaled(tint, 1.08f),
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static Color Scaled(Color color, float factor)
        {
            return new Color(Mathf.Clamp01(color.r * factor), Mathf.Clamp01(color.g * factor), Mathf.Clamp01(color.b * factor), color.a);
        }

        // ---- Slider, input, toggle

        private static void StyleSlider(Slider slider, string target)
        {
            var role = EnsureRole(slider.gameObject, UiRole.Slider);
            if (role == UiRole.Skip)
                return;

            Count(target, role);
            var background = slider.transform.Find("Background");
            if (background != null && background.TryGetComponent<Image>(out var track))
            {
                SetSprite(track, _theme.pill);
                SetColor(track, _theme.woodDark);
            }

            if (slider.fillRect != null && slider.fillRect.TryGetComponent<Image>(out var fill))
            {
                SetSprite(fill, _theme.pill);
                SetColor(fill, _theme.ember);
            }

            if (slider.handleRect != null && slider.handleRect.TryGetComponent<Image>(out var handle))
            {
                SetSprite(handle, _theme.knob, Image.Type.Simple);
                SetColor(handle, Color.white);
                if (slider.targetGraphic != handle)
                {
                    slider.targetGraphic = handle;
                    Dirty(slider);
                }
            }

            SetColors(slider, TintBlock(Color.white));
        }

        private static void StyleInput(TMP_InputField input, string target)
        {
            var role = EnsureRole(input.gameObject, UiRole.Input);
            if (role == UiRole.Skip)
                return;

            Count(target, role);
            if (input.TryGetComponent<Image>(out var image))
            {
                SetSprite(image, _theme.knob);
                SetColor(image, Color.white);
            }

            if (input.caretColor != _theme.ink)
            {
                input.caretColor = _theme.ink;
                Dirty(input);
            }

            var selection = new Color(_theme.ember.r, _theme.ember.g, _theme.ember.b, 0.4f);
            if (input.selectionColor != selection)
            {
                input.selectionColor = selection;
                Dirty(input);
            }
        }

        private static void StyleToggle(Toggle toggle, string target)
        {
            var role = EnsureRole(toggle.gameObject, UiRole.Input);
            if (role == UiRole.Skip)
                return;

            Count(target, role);
            if (toggle.targetGraphic is Image background)
            {
                SetSprite(background, _theme.knob);
                SetColor(background, Color.white);
            }

            if (toggle.graphic != null)
                SetColor(toggle.graphic, _theme.leafDark);
        }

        // ---- Plain images (panels, frames, headers, backdrops)

        private static void StyleImage(Transform t, Image image, string target)
        {
            // Game icons and stamps of the HUD are made and driven by HudPlateTools / MatchHUD / MicIndicator.
            if (t.name == "Icon" || t.name == "Stamp")
                return;

            // Stone-overlay progress bar: colour only (the game drives its fill).
            if (t.name == "BarBack")
            {
                SetColor(image, _theme.woodDark);
                return;
            }

            if (t.name == "BarFill" || (t.name == "Fill" && t.parent != null && t.parent.name == "StaminaBar"))
            {
                SetColor(image, _theme.ember);
                return;
            }

            if (t.name == "StaminaBar")
            {
                SetColor(image, WithAlpha(_theme.woodDark, 0.85f));
                return;
            }

            // The voice badge is a pill whose colour MicIndicator sets (open / muted); the wood button art would muddy it.
            if (t.name == "MicBadge")
            {
                SetSprite(image, _theme.pill, Image.Type.Sliced);
                return;
            }

            // The interaction prompt floats over the game: a dark rounded pill behind cream text.
            if (t.name == "Prompt")
            {
                SetSprite(image, _theme.pill, Image.Type.Sliced);
                SetColor(image, _theme.night80);
                return;
            }

            var existing = t.GetComponent<UiStyleRole>();
            UiRole role;
            if (existing != null)
            {
                role = existing.Role;
            }
            else
            {
                var deduced = DeduceImageRole(t, image);
                if (deduced == null)
                {
                    Unclassified.Add($"{target}: Image '{PathOf(t)}'");
                    return;
                }

                role = EnsureRole(t.gameObject, deduced.Value);
            }

            if (role == UiRole.Skip)
                return;

            Count(target, role);
            StyleImageByRole(t, image, role);
        }

        private static void StyleImageByRole(Transform t, Image image, UiRole role)
        {
            switch (role)
            {
                case UiRole.Backdrop:
                    if (t.name == "Lobby" || t.name == "LobbyBackground")
                    {
                        SetSprite(image, _theme.backdrop, Image.Type.Simple);
                        SetColor(image, Color.white);
                    }
                    else
                    {
                        SetSprite(image, null);
                        SetColor(image, _theme.shade);
                    }

                    // The vignette must not eat clicks; every other backdrop blocks what lies behind the modal.
                    var blocks = t.name != "Vignette";
                    if (image.raycastTarget != blocks)
                    {
                        image.raycastTarget = blocks;
                        Dirty(image);
                    }

                    break;
                case UiRole.Panel:
                    SetSprite(image, _theme.panel);
                    SetColor(image, Color.white);
                    break;
                case UiRole.Frame:
                    SetSprite(image, _theme.frame);
                    SetColor(image, Color.white);
                    break;
                case UiRole.Header:
                    SetSprite(image, _theme.header);
                    SetColor(image, Color.white);
                    break;
                case UiRole.Input:
                    SetSprite(image, _theme.knob);
                    SetColor(image, Color.white);
                    break;
                case UiRole.Button:
                case UiRole.ButtonConfirm:
                case UiRole.ButtonDanger:
                    SetSprite(image, _theme.button);
                    SetColor(image, Color.white);
                    break;
            }
        }

        private static UiRole? DeduceImageRole(Transform t, Image image)
        {
            var lower = t.name.ToLowerInvariant();
            if (t.name == "Lobby" || t.name == "LobbyBackground")
                return IsFullStretch(t) ? UiRole.Backdrop : null;
            if (lower.Contains("backdrop") || lower == "dim")
                return UiRole.Backdrop;
            if (lower.StartsWith("header"))
                return UiRole.Header;
            if (lower == "window" || lower == "rolecard")
                return UiRole.Frame;
            if (lower == "bg")
                return t.parent != null && (t.parent.name == "RoleCard" || t.parent.name == "EndScreen") ? UiRole.Frame : UiRole.Panel;
            if (lower == "panel" || lower == "card")
                return UiRole.Panel;
            if (lower == "rowtemplate")
                return UiRole.Input;
            if (lower == "micbadge")
                return UiRole.Button;
            if (lower.Contains("overlay"))
                return null;
            if (IsFullStretch(t) && image.color.a < 0.95f)
                return UiRole.Backdrop;
            return null;
        }

        // ---- Texts

        private static void StyleText(Transform t, TMP_Text text, string target)
        {
            if (text.text == WaitingOld)
                SetText(text, WaitingNew);

            var buttonParent = t.GetComponentInParent<Button>(true);
            var inputParent = t.GetComponentInParent<TMP_InputField>(true);
            Color color;
            var outline = false;
            var titleFont = false;
            var applyColor = !RuntimeColorNames.Contains(t.name) && !(t.parent != null && t.parent.name == "Prompt");

            if (buttonParent != null)
            {
                var role = buttonParent.TryGetComponent<UiStyleRole>(out var buttonRole) ? buttonRole.Role : UiRole.Button;
                if (role == UiRole.Skip)
                    return;
                // The plain wood button has a light fill: ink reads best. Tinted variants are dark: cream with an outline.
                outline = role != UiRole.Button;
                color = outline ? _theme.cream : _theme.ink;
                titleFont = true;
            }
            else if (inputParent != null)
            {
                color = t.name == "Placeholder" ? WithAlpha(_theme.ink, 0.45f) : _theme.ink;
            }
            else
            {
                var onPaper = HasAncestorRole(t, UiRole.Panel, UiRole.Frame);
                var inHeader = HasAncestorRole(t, UiRole.Header);
                var role = EnsureRole(t.gameObject, DeduceTextRole(t, onPaper));
                if (role == UiRole.Skip)
                    return;

                Count(target, role);
                titleFont = role == UiRole.Title || role == UiRole.Subtitle;
                switch (role)
                {
                    case UiRole.BodyOnPaper:
                        color = _theme.ink;
                        break;
                    case UiRole.Hint when onPaper && !inHeader:
                        color = WithAlpha(_theme.ink, 0.7f);
                        break;
                    case UiRole.Title:
                    case UiRole.Subtitle:
                        outline = inHeader || !onPaper;
                        color = outline ? _theme.cream : _theme.ink;
                        break;
                    default:
                        outline = inHeader || !onPaper;
                        color = outline ? _theme.cream : _theme.ink;
                        break;
                }
            }

            if (outline && applyColor == false)
                outline = text.color == _theme.cream;

            var font = titleFont ? _theme.titleFont : _theme.bodyFont;
            if (font != null)
            {
                if (text.font != font)
                {
                    text.font = font;
                    Dirty(text);
                }

                var outlineMaterial = titleFont ? _theme.titleOutline : _theme.bodyOutline;
                var material = outline && outlineMaterial != null ? outlineMaterial : font.material;
                if (text.fontSharedMaterial != material)
                {
                    text.fontSharedMaterial = material;
                    Dirty(text);
                }
            }

            if (applyColor)
                SetColor(text, color);
        }

        private static UiRole DeduceTextRole(Transform t, bool onPaper)
        {
            var lower = t.name.ToLowerInvariant();
            if (lower.Contains("subtitle"))
                return UiRole.Subtitle;
            if (lower.Contains("title"))
                return UiRole.Title;
            if (lower.Contains("hint") || HasAncestorNamed(t, "ControlsHint"))
                return UiRole.Hint;
            return onPaper ? UiRole.BodyOnPaper : UiRole.Body;
        }

        // ---- Modals and lobby background

        // Makes sure each modal window has a dim behind it that appears and disappears with the window.
        private static void EnsureModals(Transform top, string target)
        {
            foreach (var card in top.GetComponentsInChildren<RoleCard>(true))
                EnsureModal(ObjectField(card, "_card"), target);
            foreach (var hud in top.GetComponentsInChildren<MatchHUD>(true))
                EnsureModal(ObjectField(hud, "_endScreen"), target);
            foreach (var survey in top.GetComponentsInChildren<PostMatchSurvey>(true))
                EnsureModal(ObjectField(survey, "_panel"), target);
            // The credits board of the main menu: dim the menu behind it.
            var credits = FindDeep(top, "Credits");
            if (credits != null && credits.parent != null && credits.parent.name == "MainMenu")
                EnsureModal(credits.gameObject, target);
        }

        private static void EnsureModal(GameObject toggled, string target)
        {
            if (toggled == null)
                return;

            var rect = toggled.transform as RectTransform;
            if (rect == null || rect.parent == null)
                return;

            if (IsFullStretch(rect))
            {
                // The window covers the screen: the dim lives inside it (own Image, or a first child).
                if (toggled.TryGetComponent<UiStyleRole>(out var own) && own.Role == UiRole.Backdrop)
                    return;
                foreach (Transform child in rect)
                {
                    if (child.TryGetComponent<UiStyleRole>(out var childRole) && childRole.Role == UiRole.Backdrop)
                        return;
                }

                CreateBackdrop(rect, 0, target);
                return;
            }

            var parent = rect.parent;
            if (parent.GetComponent<Canvas>() == null && !IsFullStretch(parent))
            {
                Unclassified.Add($"{target}: modal '{PathOf(rect)}' has no full-screen parent for a backdrop");
                return;
            }

            var existing = parent.Find("Backdrop");
            if (existing == null)
                existing = CreateBackdrop(parent, rect.GetSiblingIndex(), target).transform;

            var link = toggled.GetComponent<UiBackdropLink>();
            if (link == null)
            {
                link = toggled.AddComponent<UiBackdropLink>();
                _changes++;
            }

            if (link.Backdrop != existing.gameObject)
            {
                link.Backdrop = existing.gameObject;
                Dirty(link);
            }

            StyleImageByRole(existing, existing.GetComponent<Image>(), UiRole.Backdrop);

            // Saved hidden: the link shows it when the window becomes active (OnEnable), so a window that a script
            // deactivates before its own OnEnable ever runs cannot leave the dim on screen.
            if (existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(false);
                Dirty(existing.gameObject);
            }
        }

        private static GameObject CreateBackdrop(Transform parent, int siblingIndex, string target)
        {
            var go = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(UiStyleRole)) { layer = UiLayer };
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(siblingIndex);
            if (parent.GetComponent<LayoutGroup>() != null)
                go.AddComponent<LayoutElement>().ignoreLayout = true;

            go.GetComponent<UiStyleRole>().Role = UiRole.Backdrop;
            StyleImageByRole(rect, go.GetComponent<Image>(), UiRole.Backdrop);
            Count(target, UiRole.Backdrop);
            _changes++;
            return go;
        }

        // StartMatch_Canvas lobby: MainMenuBG plus a dark vignette instead of flat black.
        private static void EnsureLobbyBackground(Transform top, string target)
        {
            if (top.name != "StartMatch_Canvas")
                return;

            var lobby = FindDeep(top, "Lobby");
            if (lobby == null)
            {
                Notes.Add($"{target}: 'Lobby' not found");
                return;
            }

            if (!IsFullStretch(lobby))
            {
                Unclassified.Add($"{target}: Lobby is not full-screen, background not applied");
                return;
            }

            Image background;
            if (lobby.TryGetComponent<Image>(out var own))
            {
                background = own;
                EnsureRole(lobby.gameObject, UiRole.Backdrop);
                StyleImageByRole(lobby, background, UiRole.Backdrop);
            }
            else
            {
                var child = lobby.Find("LobbyBackground");
                if (child == null)
                {
                    var created = CreateBackdrop(lobby, 0, target);
                    created.name = "LobbyBackground";
                    child = created.transform;
                }

                StyleImageByRole(child, child.GetComponent<Image>(), UiRole.Backdrop);
                background = child.GetComponent<Image>();
            }

            var vignette = lobby.Find("Vignette");
            if (vignette == null)
            {
                var created = CreateBackdrop(lobby, background.transform == lobby ? 0 : 1, target);
                created.name = "Vignette";
                StyleImageByRole(created.transform, created.GetComponent<Image>(), UiRole.Backdrop);
            }
        }

        // ---------------------------------------------------------------- Write helpers (write only when different)

        private static UiRole EnsureRole(GameObject go, UiRole deduced)
        {
            if (go.TryGetComponent<UiStyleRole>(out var existing))
                return existing.Role;

            var marker = go.AddComponent<UiStyleRole>();
            marker.Role = deduced;
            _changes++;
            return deduced;
        }

        private static void SetSprite(Image image, Sprite sprite, Image.Type? forcedType = null)
        {
            var type = forcedType ?? (sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple);
            if (image.sprite == sprite && image.type == type && !image.preserveAspect)
                return;

            image.sprite = sprite;
            image.type = type;
            image.preserveAspect = false;
            Dirty(image);
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic.color == color)
                return;

            graphic.color = color;
            Dirty(graphic);
        }

        private static void SetColors(Selectable selectable, ColorBlock block)
        {
            if (selectable.colors.Equals(block))
                return;

            selectable.colors = block;
            Dirty(selectable);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text.text == value)
                return;

            text.text = value;
            Dirty(text);
        }

        private static void Dirty(Object target)
        {
            EditorUtility.SetDirty(target);
            _changes++;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ---------------------------------------------------------------- Queries

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }

            return null;
        }

        private static bool HasAncestorRole(Transform t, params UiRole[] roles)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (!p.TryGetComponent<UiStyleRole>(out var marker))
                    continue;
                foreach (var role in roles)
                {
                    if (marker.Role == role)
                        return true;
                }
            }

            return false;
        }

        private static bool HasAncestorNamed(Transform t, string name)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (p.name == name)
                    return true;
            }

            return false;
        }

        private static bool IsFullStretch(Transform t)
        {
            if (t is not RectTransform rect)
                return false;

            return rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
                   && rect.offsetMin.sqrMagnitude < 1f && rect.offsetMax.sqrMagnitude < 1f;
        }

        private static GameObject ObjectField(Component component, string property)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(property);
            return prop != null ? prop.objectReferenceValue as GameObject : null;
        }

        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        private static void Count(string target, UiRole role)
        {
            var key = $"{target} | {role}";
            Counts.TryGetValue(key, out var n);
            Counts[key] = n + 1;
        }

        // ---------------------------------------------------------------- Report

        private static void WriteReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"UI theme applied. Writes this pass: {_changes} (0 = already up to date).");
            sb.AppendLine();
            sb.AppendLine("Elements by target and role:");
            var keys = new List<string>(Counts.Keys);
            keys.Sort();
            foreach (var key in keys)
                sb.AppendLine($"  {key}: {Counts[key]}");

            sb.AppendLine();
            sb.AppendLine($"Not classified ({Unclassified.Count}):");
            foreach (var line in Unclassified)
                sb.AppendLine("  " + line);

            if (Notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Notes:");
                foreach (var line in Notes)
                    sb.AppendLine("  " + line);
            }

            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"{LogPrefix} {sb}");
        }
    }
}
