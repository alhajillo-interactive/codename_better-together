// Editor-only builder for the in-game HUD (C0.7). Run it from the menu GnoMore/Construir HUD de juego: it opens
// Prefabs/UI/MatchHUD.prefab, creates (or reuses) the uGUI/TMP children of the crosshair, interaction prompt, gaze
// feedback, microphone badge and screen flash, adds and wires InteractionPrompt, GazeFeedbackHUD, MicIndicator and
// ScreenFlash on the prefab root, sets the CanvasScaler to 1920x1080 and, if it does not exist, writes the radial
// vignette sprite Art/UI/vignette_radial.png. It is idempotent: elements that already exist are kept as they are
// (so tweaks made in the editor survive); only the references are wired again.
using System.IO;
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class GameHudBuilder
    {
        private const string LogPrefix = "[GameHudBuilder]";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/MatchHUD.prefab";
        private const string ConePath = "Assets/_Project/Art/UI/vignette_radial.png";
        private const string GnomePath = "Assets/_Project/Art/UI/vignette_radial_gnome.png";
        private const string FocusPath = "Assets/_Project/Art/UI/vignette_radial_focus.png";
        private const int UiLayer = 5;

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Vector2 TopCenter = new(0.5f, 1f);
        private static readonly Vector2 BottomCenter = new(0.5f, 0f);
        private static readonly Vector2 BottomLeft = Vector2.zero;

        [MenuItem("GnoMore/Construir HUD de juego")]
        public static void Build()
        {
            if (!File.Exists(PrefabPath))
            {
                Debug.LogError($"{LogPrefix} Prefab not found: {PrefabPath}");
                return;
            }

            var cone = EnsureVignetteSprite(ConePath, HudSupport.RampRatioCone);
            var gnome = EnsureVignetteSprite(GnomePath, HudSupport.RampRatioGnome);
            var focus = EnsureVignetteSprite(FocusPath, HudSupport.RampRatioFocus);
            if (cone == null || gnome == null || focus == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BuildContents(root, cone, gnome, focus);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} HUD built in {PrefabPath}. Remove the old InteractionPrompt, GazeFeedbackHUD, MicIndicator " +
                      "and ScreenFlash components from the 'HUD' object of each scene (they are replaced by the ones on the prefab).");
        }

        #region Sprite

        // Writes the PNG when it is missing or no longer matches HudSupport (size or ramp), and makes sure it imports as a sprite.
        private static Sprite EnsureVignetteSprite(string assetPath, float rampRatio)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!File.Exists(fullPath) || !PngMatches(fullPath, rampRatio))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, GenerateVignettePng(rampRatio));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Debug.Log($"{LogPrefix} Generated {assetPath}");
            }

            ConfigureImporter(assetPath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
                Debug.LogError($"{LogPrefix} {assetPath} did not import as a Sprite.");

            return sprite;
        }

        // Alpha of the gradient at `distance` pixels from the centre: 0 inside the clear radius, smooth ramp, 1 at the ramp end.
        private static float VignetteAlpha(float distance, float rampRatio)
        {
            var clear = HudSupport.ClearRadiusPixels;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clear, clear * rampRatio, distance));
        }

        // True when the file has the expected size and the alpha halfway up the ramp is the expected one.
        private static bool PngMatches(string fullPath, float rampRatio)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
                    return false;

                const int size = HudSupport.SpritePixels;
                if (texture.width != size || texture.height != size)
                    return false;

                var clear = HudSupport.ClearRadiusPixels;
                var probe = clear * (1f + rampRatio) * 0.5f;
                var center = (size - 1) * 0.5f;
                var x = Mathf.RoundToInt(center + probe);
                var y = size / 2;
                var expected = VignetteAlpha(new Vector2(x - center, y - center).magnitude, rampRatio);
                return Mathf.Abs(texture.GetPixel(x, y).a - expected) < 0.02f;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // 512x512, white, alpha 0 up to ClearRadiusPixels from the centre, smooth ramp to 1 at clear * rampRatio, 1 beyond.
        private static byte[] GenerateVignettePng(float rampRatio)
        {
            const int size = HudSupport.SpritePixels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = new Vector2(x - center, y - center).magnitude;
                    var alpha = VignetteAlpha(distance, rampRatio);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        private static void ConfigureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            var border = HudSupport.BorderPixels;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            var correct = importer.textureType == TextureImporterType.Sprite
                          && importer.spriteImportMode == SpriteImportMode.Single
                          && Mathf.Approximately(importer.spritePixelsPerUnit, 100f)
                          && importer.spriteBorder == new Vector4(border, border, border, border)
                          && !importer.mipmapEnabled
                          && importer.alphaIsTransparency
                          && importer.wrapMode == TextureWrapMode.Clamp
                          && importer.textureCompression == TextureImporterCompression.Uncompressed
                          && settings.spriteMeshType == SpriteMeshType.FullRect;
            if (correct)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = HudSupport.SpritePixels;

            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        #endregion

        #region Prefab contents

        private static void BuildContents(GameObject root, Sprite coneSprite, Sprite gnomeSprite, Sprite focusSprite)
        {
            var rootRect = (RectTransform)root.transform;
            ConfigureScaler(root);
            var font = FindFont(root);

            // Back layer (behind the match HUD): vignettes.
            var gnomeVignette = BuildVignette(rootRect, "GnomeVignette", gnomeSprite, new Color(1f, 1f, 1f, 0f), 0);
            var focusVignette = BuildVignette(rootRect, "FocusVignette", focusSprite, new Color(0f, 0f, 0f, 0f), 1);
            var coneVignette = BuildVignette(rootRect, "CamperConeVignette", coneSprite, new Color(0.3f, 0.2f, 0.5f, 0f), 2);

            // Front layer: crosshair, prompt, warnings, overlays, badges.
            var crosshair = BuildCrosshair(rootRect);
            BuildPrompt(rootRect, font, out var promptRoot, out var promptText);
            var watched = BuildWatchedWarning(rootRect, font);
            BuildStoneOverlay(rootRect, font, out var stoneRoot, out var stoneTitle, out var stoneCounter, out var stoneBarFill);
            BuildStamina(rootRect, font, out var staminaRoot, out var staminaFill, out var staminaFillImage,
                out var staminaThreshold, out var exhaustedLabel, out var hintLabel);
            BuildMicBadge(rootRect, font, out var micBadge, out var micBackground, out var micLabel);
            var flash = BuildFlash(rootRect);

            Wire(GetOrAdd<InteractionPrompt>(root),
                ("_crosshair", crosshair),
                ("_promptRoot", promptRoot),
                ("_promptText", promptText),
                ("_watchedWarning", watched));

            Wire(GetOrAdd<GazeFeedbackHUD>(root),
                ("_gnomeVignette", gnomeVignette),
                ("_stoneRoot", stoneRoot),
                ("_stoneTitle", stoneTitle),
                ("_stoneCounter", stoneCounter),
                ("_stoneBarFill", stoneBarFill),
                ("_focusVignette", focusVignette),
                ("_coneVignette", coneVignette),
                ("_staminaRoot", staminaRoot),
                ("_staminaFill", staminaFill),
                ("_staminaFillImage", staminaFillImage),
                ("_staminaThreshold", staminaThreshold),
                ("_exhaustedLabel", exhaustedLabel),
                ("_hintLabel", hintLabel));

            Wire(GetOrAdd<MicIndicator>(root),
                ("_badge", micBadge),
                ("_background", micBackground),
                ("_label", micLabel));

            Wire(GetOrAdd<ScreenFlash>(root), ("_image", flash));

            // Draw order at the top: the end screen covers the crosshair and the prompt, and the flash covers everything.
            var endScreen = rootRect.Find("EndScreen");
            if (endScreen != null)
                endScreen.SetAsLastSibling();

            flash.transform.SetAsLastSibling();
        }

        private static void ConfigureScaler(GameObject root)
        {
            var scaler = GetOrAdd<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // HudSupport.BorderUnits assumes 100 reference pixels per unit.
            scaler.referencePixelsPerUnit = 100f;
        }

        private static TMP_FontAsset FindFont(GameObject root)
        {
            var existing = root.GetComponentInChildren<TMP_Text>(true);
            return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        private static Image BuildVignette(RectTransform parent, string name, Sprite sprite, Color color, int siblingIndex)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprite;

            // Not cosmetic: the runtime sizing in HudSupport assumes a sliced Image with this border multiplier.
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = HudSupport.BorderMultiplier;
            if (!created)
                return image;

            Place(rect, Center, Center, Center, Vector2.zero, Vector2.one * HudSupport.VignetteSizeFor(300f));
            image.color = color;
            image.raycastTarget = false;
            image.enabled = false;
            rect.SetSiblingIndex(siblingIndex);
            return image;
        }

        private static Image BuildCrosshair(RectTransform parent)
        {
            var rect = Child(parent, "Crosshair", out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (!created)
                return image;

            Place(rect, Center, Center, Center, Vector2.zero, new Vector2(6f, 6f));
            image.color = new Color(1f, 1f, 1f, 0.8f);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private static void BuildPrompt(RectTransform parent, TMP_FontAsset font, out GameObject promptRoot, out TMP_Text promptText)
        {
            var rect = Child(parent, "Prompt", out var created);
            promptRoot = rect.gameObject;
            var background = GetOrAdd<Image>(promptRoot);
            if (created)
            {
                Place(rect, Center, Center, TopCenter, new Vector2(0f, -40f), new Vector2(560f, 0f));
                background.color = new Color(0f, 0f, 0f, 0.45f);
                background.raycastTarget = false;

                var layout = GetOrAdd<VerticalLayoutGroup>(promptRoot);
                layout.padding = new RectOffset(16, 16, 10, 10);
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                var fitter = GetOrAdd<ContentSizeFitter>(promptRoot);
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            promptText = Text(rect, "Text", "[E] Interactuar", 28f, Color.white, TextAlignmentOptions.Top, font, true, out _, out _);
            if (created)
                promptRoot.SetActive(false);
        }

        private static TMP_Text BuildWatchedWarning(RectTransform parent, TMP_FontAsset font)
        {
            var text = Text(parent, "WatchedWarning", "¡El campista te está mirando! No te muevas", 36f,
                new Color(1f, 0.25f, 0.2f), TextAlignmentOptions.Center, font, false, out var rect, out var created);
            if (created)
            {
                Place(rect, TopCenter, TopCenter, TopCenter, new Vector2(0f, -162f), new Vector2(1100f, 60f));
                rect.gameObject.SetActive(false);
            }

            return text;
        }

        private static void BuildStoneOverlay(RectTransform parent, TMP_FontAsset font, out GameObject stoneRoot,
            out TMP_Text title, out TMP_Text counter, out RectTransform barFill)
        {
            var rect = Child(parent, "StoneOverlay", out var created);
            stoneRoot = rect.gameObject;
            if (created)
                Stretch(rect);

            var anchorMin = new Vector2(0f, 0.6f);
            var anchorMax = new Vector2(1f, 0.6f);

            title = Text(rect, "Title", "¡Petrificado! Machaca E", 52f, Color.white, TextAlignmentOptions.Center, font, false,
                out var titleRect, out var titleCreated);
            if (titleCreated)
                Place(titleRect, anchorMin, anchorMax, Center, Vector2.zero, new Vector2(0f, 70f));

            counter = Text(rect, "Counter", "0/0", 30f, Color.white, TextAlignmentOptions.Center, font, false,
                out var counterRect, out var counterCreated);
            if (counterCreated)
                Place(counterRect, anchorMin, anchorMax, Center, new Vector2(0f, -60f), new Vector2(0f, 40f));

            var backRect = Child(rect, "BarBack", out var backCreated);
            var back = GetOrAdd<Image>(backRect.gameObject);
            if (backCreated)
            {
                var barAnchor = new Vector2(0.5f, 0.6f);
                Place(backRect, barAnchor, barAnchor, Center, new Vector2(0f, -105f), new Vector2(360f, 16f));
                back.color = new Color(0f, 0f, 0f, 0.5f);
                back.raycastTarget = false;
            }

            barFill = Child(backRect, "BarFill", out var fillCreated);
            var fill = GetOrAdd<Image>(barFill.gameObject);
            if (fillCreated)
            {
                Stretch(barFill);
                barFill.anchorMax = new Vector2(0f, 1f);
                fill.color = new Color(1f, 1f, 1f, 0.9f);
                fill.raycastTarget = false;
            }

            if (created)
                stoneRoot.SetActive(false);
        }

        private static void BuildStamina(RectTransform parent, TMP_FontAsset font, out GameObject staminaRoot,
            out RectTransform fillRect, out Image fillImage, out RectTransform threshold, out TMP_Text exhaustedLabel,
            out TMP_Text hintLabel)
        {
            var rect = Child(parent, "StaminaBar", out var created);
            staminaRoot = rect.gameObject;
            var back = GetOrAdd<Image>(staminaRoot);
            if (created)
            {
                Place(rect, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 70f), new Vector2(260f, 14f));
                back.color = new Color(0f, 0f, 0f, 0.5f);
                back.raycastTarget = false;
            }

            fillRect = Child(rect, "Fill", out var fillCreated);
            fillImage = GetOrAdd<Image>(fillRect.gameObject);
            if (fillCreated)
            {
                Stretch(fillRect);
                fillImage.color = new Color(0.6f, 0.5f, 1f, 0.95f);
                fillImage.raycastTarget = false;
            }

            threshold = Child(rect, "ThresholdMark", out var markCreated);
            var mark = GetOrAdd<Image>(threshold.gameObject);
            if (markCreated)
            {
                // Vertical stripe 2 wide that sticks out 3 above and below; GazeFeedbackHUD moves its anchors.
                Place(threshold, new Vector2(0.35f, 0f), new Vector2(0.35f, 1f), Center, Vector2.zero, new Vector2(2f, 6f));
                mark.color = new Color(1f, 1f, 1f, 0.85f);
                mark.raycastTarget = false;
            }

            exhaustedLabel = Text(rect, "ExhaustedLabel", "Mirada agotada", 20f, new Color(1f, 0.2f, 0.2f, 0.95f),
                TextAlignmentOptions.Center, font, false, out var exhaustedRect, out var exhaustedCreated);
            if (exhaustedCreated)
            {
                Place(exhaustedRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(0f, 28f));
                exhaustedRect.gameObject.SetActive(false);
            }

            hintLabel = Text(rect, "HintLabel", "[Clic der.] Mirada", 20f, Color.white, TextAlignmentOptions.MidlineLeft, font,
                false, out var hintRect, out var hintCreated);
            if (hintCreated)
            {
                Place(hintRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(260f, 28f));
                hintRect.gameObject.SetActive(false);
            }

            if (created)
                staminaRoot.SetActive(false);
        }

        private static void BuildMicBadge(RectTransform parent, TMP_FontAsset font, out GameObject badge, out Image background,
            out TMP_Text label)
        {
            var rect = Child(parent, "MicBadge", out var created);
            badge = rect.gameObject;
            background = GetOrAdd<Image>(badge);
            if (created)
            {
                Place(rect, BottomLeft, BottomLeft, BottomLeft, new Vector2(12f, 12f), new Vector2(180f, 38f));
                background.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
                background.raycastTarget = false;
            }

            label = Text(rect, "Label", "MIC OFF   [M]", 20f, Color.white, TextAlignmentOptions.Center, font, false,
                out var labelRect, out var labelCreated);
            if (labelCreated)
                Stretch(labelRect);

            if (created)
                badge.SetActive(false);
        }

        private static Image BuildFlash(RectTransform parent)
        {
            var rect = Child(parent, "Flash", out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (created)
            {
                Stretch(rect);
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;
                image.enabled = false;
            }

            return image;
        }

        #endregion

        #region Helpers

        // Finds the child by name or creates an empty UI object (RectTransform on layer UI) under the parent.
        private static RectTransform Child(Transform parent, string name, out bool created)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                created = false;
                return (RectTransform)existing;
            }

            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            go.transform.SetParent(parent, false);
            created = true;
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, TMP_FontAsset font, bool wrap, out RectTransform rect, out bool created)
        {
            rect = Child(parent, name, out created);
            var text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
            if (!created)
                return text;

            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Center;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();

        // Assigns serialized object references by field name; logs an error for a field that does not exist.
        private static void Wire(Component target, params (string field, Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in references)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"{LogPrefix} {target.GetType().Name} has no serialized field '{field}'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion
    }
}
