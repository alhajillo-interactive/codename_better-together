// Editor-only builder for the in-game HUD (C0.7). Run it from the menu GnoMore/Construir HUD de juego: it opens
// Prefabs/UI/MatchHUD.prefab, creates (or reuses) the uGUI/TMP children of the crosshair, interaction prompt, gaze
// feedback, microphone badge and screen flash, adds and wires InteractionPrompt, GazeFeedbackHUD, MicIndicator and
// ScreenFlash on the prefab root and sets the CanvasScaler to 1920x1080. C8.2: the gaze feedback is the camera viewfinder
// (Viewfinder, PlatesRow, GnomeThreat, FrozenView, AlbumSlot), built with the sprites of the UiTheme (run GnoMore/Aplicar
// estilo de UI first); the old vignettes, stone overlay and stamina bar are removed. It is idempotent: elements that
// already exist are kept as they are (so tweaks made in the editor survive); only the references are wired again.
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
        private const string ThemePath = "Assets/_Project/Resources/UiTheme.asset";
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

            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            if (theme == null || theme.viewfinderCorner == null || theme.focusBracket == null || theme.plate == null
                || theme.lensGlint == null || theme.ring == null)
            {
                Debug.LogError($"{LogPrefix} The UiTheme at {ThemePath} is missing or has no viewfinder sprites: run GnoMore/Aplicar estilo de UI first.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BuildContents(root, theme);
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

        #region Prefab contents

        // Old pieces replaced by C8.2 (vignettes, stone overlay, stamina bar): removed once, so the builder stays idempotent.
        private static readonly string[] ObsoleteParts =
            { "GnomeVignette", "FocusVignette", "CamperConeVignette", "StaminaBar", "StoneOverlay" };

        private static void BuildContents(GameObject root, UiTheme theme)
        {
            var rootRect = (RectTransform)root.transform;
            ConfigureScaler(root);
            var font = FindFont(root);

            foreach (var part in ObsoleteParts)
            {
                var old = rootRect.Find(part);
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
            }

            // Back layer (behind the match HUD): the viewfinder and the gnome's widgets.
            var viewfinder = BuildViewfinder(rootRect, theme, font);
            var plates = BuildPlatesRow(rootRect, theme);
            var threat = BuildThreatGlint(rootRect, theme);
            var frozen = BuildFrozenView(rootRect, theme, font);
            var album = BuildAlbumSlot(rootRect, theme);
            BuildTeammates(rootRect, theme, font);

            // Front layer: crosshair, prompt, warnings, badges.
            var crosshair = BuildCrosshair(rootRect);
            BuildPrompt(rootRect, font, out var promptRoot, out var promptText);
            var watched = BuildWatchedWarning(rootRect, font);
            BuildMicBadge(rootRect, font, out var micBadge, out var micBackground, out var micLabel);
            var flash = BuildFlash(rootRect);

            Wire(GetOrAdd<InteractionPrompt>(root),
                ("_crosshair", crosshair),
                ("_promptRoot", promptRoot),
                ("_promptText", promptText),
                ("_watchedWarning", watched));

            Wire(GetOrAdd<GazeFeedbackHUD>(root),
                ("_viewfinderRoot", viewfinder.Root),
                ("_viewfinderGroup", viewfinder.Group),
                ("_dimRoot", viewfinder.Dim),
                ("_cornerRoot", viewfinder.Corners),
                ("_thirdsRoot", viewfinder.Thirds),
                ("_bracketRoot", viewfinder.Brackets),
                ("_focusedLabel", viewfinder.FocusedLabel),
                ("_platesRow", plates),
                ("_shootPrompt", viewfinder.ShootPrompt),
                ("_shootText", viewfinder.ShootText),
                ("_albumRoot", album.Frame),
                ("_albumGroup", album.Group),
                ("_albumImage", album.Photo),
                ("_threatGlint", threat),
                ("_frozenRoot", frozen.Root),
                ("_frozenFrame", frozen.Frame),
                ("_ringRoot", frozen.RingRoot),
                ("_ringImage", frozen.RingFill),
                ("_shakeLabel", frozen.Label));

            Wire(GetOrAdd<MicIndicator>(root),
                ("_badge", micBadge),
                ("_background", micBackground),
                ("_label", micLabel));

            Wire(GetOrAdd<ScreenFlash>(root), ("_image", flash));

            // C6.2a: elastic objectives plate, line stamps, toast pill and the mic badge in the safe zone.
            HudPlateTools.Ensure(root, theme);

            // Draw order at the top: the end screen covers the crosshair and the prompt, and the flash covers everything.
            var endScreen = rootRect.Find("EndScreen");
            if (endScreen != null)
                endScreen.SetAsLastSibling();

            flash.transform.SetAsLastSibling();
        }

        private readonly struct ViewfinderParts
        {
            public readonly RectTransform Root;
            public readonly CanvasGroup Group;
            public readonly RectTransform Dim;
            public readonly RectTransform Corners;
            public readonly RectTransform Thirds;
            public readonly RectTransform Brackets;
            public readonly RectTransform FocusedLabel;
            public readonly RectTransform ShootPrompt;
            public readonly TMP_Text ShootText;

            public ViewfinderParts(RectTransform root, CanvasGroup group, RectTransform dim, RectTransform corners, RectTransform thirds,
                RectTransform brackets, RectTransform focusedLabel, RectTransform shootPrompt, TMP_Text shootText)
            {
                Root = root;
                Group = group;
                Dim = dim;
                Corners = corners;
                Thirds = thirds;
                Brackets = brackets;
                FocusedLabel = focusedLabel;
                ShootPrompt = shootPrompt;
                ShootText = shootText;
            }
        }

        private readonly struct FrozenParts
        {
            public readonly GameObject Root;
            public readonly RectTransform Frame;
            public readonly RectTransform RingRoot;
            public readonly Image RingFill;
            public readonly TMP_Text Label;

            public FrozenParts(GameObject root, RectTransform frame, RectTransform ringRoot, Image ringFill, TMP_Text label)
            {
                Root = root;
                Frame = frame;
                RingRoot = ringRoot;
                RingFill = ringFill;
                Label = label;
            }
        }

        private readonly struct AlbumParts
        {
            public readonly RectTransform Frame;
            public readonly CanvasGroup Group;
            public readonly RawImage Photo;

            public AlbumParts(RectTransform frame, CanvasGroup group, RawImage photo)
            {
                Frame = frame;
                Group = group;
                Photo = photo;
            }
        }

        // Camper viewfinder: Viewfinder (pop root) > Content (CanvasGroup) > Dim (4 bars), Corners (4), Thirds (4 lines),
        // Brackets (4 + the "Enfocado" pill), ShootPrompt. GazeFeedbackHUD positions every piece at runtime from the cone.
        private static ViewfinderParts BuildViewfinder(RectTransform parent, UiTheme theme, TMP_FontAsset font)
        {
            var root = Child(parent, "Viewfinder", out var created);
            if (created)
            {
                Stretch(root);
                root.SetSiblingIndex(0);
            }

            var content = Child(root, "Content", out var contentCreated);
            if (contentCreated)
                Stretch(content);
            var group = GetOrAdd<CanvasGroup>(content.gameObject);
            if (contentCreated)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var dim = CenterContainer(content, "Dim");
            Bar(dim, "Top", new Vector2(0.5f, 0f));
            Bar(dim, "Bottom", new Vector2(0.5f, 1f));
            Bar(dim, "Left", new Vector2(1f, 0.5f));
            Bar(dim, "Right", new Vector2(0f, 0.5f));

            var corners = CenterContainer(content, "Corners");
            foreach (var corner in CornerNames)
                Piece(corners, corner, theme.viewfinderCorner, Color.white, new Vector2(0f, 1f), new Vector2(110f, 110f));

            var thirds = CenterContainer(content, "Thirds");
            foreach (var line in new[] { "V1", "V2", "H1", "H2" })
                Piece(thirds, line, null, theme.cream, Center, new Vector2(2f, 2f));

            var brackets = CenterContainer(content, "Brackets");
            var signs = new[] { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
            for (var i = 0; i < CornerNames.Length; i++)
            {
                var bracket = Piece(brackets, CornerNames[i], theme.focusBracket, Color.white, new Vector2(0f, 1f), new Vector2(56f, 56f));
                bracket.rectTransform.localScale = new Vector3(signs[i].x, signs[i].y, 1f);
            }

            var label = Child(brackets, "FocusedLabel", out var labelCreated);
            var labelImage = GetOrAdd<Image>(label.gameObject);
            if (labelCreated)
            {
                Place(label, Center, Center, TopCenter, new Vector2(0f, -90f), new Vector2(220f, 46f));
                labelImage.sprite = theme.pill;
                labelImage.type = Image.Type.Sliced;
                labelImage.color = theme.ember;
                labelImage.raycastTarget = false;
                label.gameObject.SetActive(false);
            }

            var labelText = Text(label, "Text", "¡Enfocado!", 32f, theme.ink, TextAlignmentOptions.Center, font, false,
                out var labelTextRect, out var labelTextCreated);
            if (labelTextCreated)
            {
                Stretch(labelTextRect);
                ApplyTitleFont(labelText, theme, false);
            }

            var prompt = Child(content, "ShootPrompt", out var promptCreated);
            var promptImage = GetOrAdd<Image>(prompt.gameObject);
            if (promptCreated)
            {
                Place(prompt, Center, Center, BottomCenter, new Vector2(0f, -400f), new Vector2(600f, 54f));
                promptImage.sprite = theme.pill;
                promptImage.type = Image.Type.Sliced;
                promptImage.color = theme.night80;
                promptImage.raycastTarget = false;
            }

            var promptText = Text(prompt, "Text", "Clic · Disparar", 30f, theme.cream, TextAlignmentOptions.Center, font, false,
                out var promptTextRect, out var promptTextCreated);
            if (promptTextCreated)
            {
                Stretch(promptTextRect);
                ApplyTitleFont(promptText, theme, true);
            }

            if (created)
                root.gameObject.SetActive(false);

            return new ViewfinderParts(root, group, dim, corners, thirds, brackets, label, prompt, promptText);
        }

        private static readonly string[] CornerNames = { "TL", "TR", "BL", "BR" };

        // A zero-size object at the canvas centre: its children are positioned from the centre at runtime.
        private static RectTransform CenterContainer(RectTransform parent, string name)
        {
            var rect = Child(parent, name, out var created);
            if (created)
                Place(rect, Center, Center, Center, Vector2.zero, Vector2.zero);

            return rect;
        }

        private static void Bar(RectTransform parent, string name, Vector2 pivot)
        {
            Piece(parent, name, null, new Color(0f, 0f, 0f, 0.5f), pivot, new Vector2(10f, 10f));
        }

        // An Image child anchored at the centre of its parent; runtime code sets position and size.
        private static Image Piece(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pivot, Vector2 size)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (created)
            {
                Place(rect, Center, Center, pivot, Vector2.zero, size);
                image.color = color;
                image.raycastTarget = false;
            }

            if (image.sprite == null && sprite != null)
                image.sprite = sprite;

            return image;
        }

        // Three glass plates: ghost (30 %) with a Filled child; GazeFeedbackHUD moves the row.
        private static RectTransform BuildPlatesRow(RectTransform parent, UiTheme theme)
        {
            const float Plate = 60f;
            const float Gap = 10f;
            var row = Child(parent, "PlatesRow", out var created);
            if (created)
            {
                Place(row, Center, Center, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f * Plate + 2f * Gap, Plate));
                row.SetSiblingIndex(1);
            }

            for (var i = 0; i < 3; i++)
            {
                var plate = Child(row, $"Plate_{i}", out var plateCreated);
                var ghost = GetOrAdd<Image>(plate.gameObject);
                if (plateCreated)
                {
                    Place(plate, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * (Plate + Gap), 0f),
                        new Vector2(Plate, Plate));
                    ghost.color = new Color(1f, 1f, 1f, 0.3f);
                    ghost.raycastTarget = false;
                }

                if (ghost.sprite == null)
                    ghost.sprite = theme.plate;

                var fill = Child(plate, "Fill", out var fillCreated);
                var fillImage = GetOrAdd<Image>(fill.gameObject);
                if (fillCreated)
                {
                    Stretch(fill);
                    fillImage.color = Color.white;
                    fillImage.raycastTarget = false;
                }

                if (fillImage.sprite == null)
                    fillImage.sprite = theme.plate;

                if (fillImage.type != Image.Type.Filled || fillImage.fillMethod != Image.FillMethod.Horizontal)
                {
                    fillImage.type = Image.Type.Filled;
                    fillImage.fillMethod = Image.FillMethod.Horizontal;
                    fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }

            if (created)
                row.gameObject.SetActive(false);

            return row;
        }

        // Lens glint at the screen edge (gnome threat levels 1 and 2); size, colour and position are runtime.
        private static Image BuildThreatGlint(RectTransform parent, UiTheme theme)
        {
            var rect = Child(parent, "GnomeThreat", out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (created)
            {
                Place(rect, Center, Center, Center, Vector2.zero, new Vector2(52f, 52f));
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;
                image.enabled = false;
                rect.SetSiblingIndex(2);
            }

            if (image.sprite == null)
                image.sprite = theme.lensGlint;

            return image;
        }

        // Frozen gnome: sepia frame (4 edges, 24 px), break-free ring around the crosshair with an "E", and "¡Sacúdete!".
        private static FrozenParts BuildFrozenView(RectTransform parent, UiTheme theme, TMP_FontAsset font)
        {
            const float FrameWidth = 24f;
            var root = Child(parent, "FrozenView", out var created);
            if (created)
            {
                Stretch(root);
                root.SetSiblingIndex(3);
            }

            var frame = Child(root, "Frame", out var frameCreated);
            if (frameCreated)
                Stretch(frame);

            EdgeBar(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, FrameWidth), theme);
            EdgeBar(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, FrameWidth), theme);
            EdgeBar(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(FrameWidth, 0f), theme);
            EdgeBar(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(FrameWidth, 0f), theme);

            var ringRoot = Child(root, "Ring", out var ringCreated);
            if (ringCreated)
                Place(ringRoot, Center, Center, Center, Vector2.zero, new Vector2(150f, 150f));

            var back = Child(ringRoot, "Back", out var backCreated);
            var backImage = GetOrAdd<Image>(back.gameObject);
            if (backCreated)
            {
                Stretch(back);
                backImage.color = new Color(theme.night80.r, theme.night80.g, theme.night80.b, 0.5f);
                backImage.raycastTarget = false;
            }

            if (backImage.sprite == null)
                backImage.sprite = theme.ring;

            var fill = Child(ringRoot, "Fill", out var fillCreated);
            var fillImage = GetOrAdd<Image>(fill.gameObject);
            if (fillCreated)
            {
                Stretch(fill);
                fillImage.color = theme.ember;
                fillImage.raycastTarget = false;
            }

            if (fillImage.sprite == null)
                fillImage.sprite = theme.ring;

            if (fillImage.type != Image.Type.Filled || fillImage.fillMethod != Image.FillMethod.Radial360)
            {
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Radial360;
                fillImage.fillOrigin = (int)Image.Origin360.Top;
                fillImage.fillClockwise = true;
                fillImage.fillAmount = 0f;
            }

            var glyph = Text(ringRoot, "Key", "E", 64f, theme.cream, TextAlignmentOptions.Center, font, false, out var glyphRect, out var glyphCreated);
            if (glyphCreated)
            {
                Stretch(glyphRect);
                ApplyTitleFont(glyph, theme, true);
            }

            var label = Text(root, "ShakeLabel", "¡Sacúdete!", 52f, theme.cream, TextAlignmentOptions.Center, font, false,
                out var labelRect, out var labelCreated);
            if (labelCreated)
            {
                Place(labelRect, Center, Center, TopCenter, new Vector2(0f, -110f), new Vector2(700f, 76f));
                ApplyTitleFont(label, theme, true);
            }

            if (created)
                root.gameObject.SetActive(false);

            return new FrozenParts(root.gameObject, frame, ringRoot, fillImage, label);
        }

        private static void EdgeBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size,
            UiTheme theme)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            if (!created)
                return;

            Place(rect, anchorMin, anchorMax, pivot, Vector2.zero, size);
            image.color = theme.photoSepia;
            image.raycastTarget = false;
        }

        // Album slot (bottom right): a photo frame with a RawImage that C8.3 fills through GazeFeedbackHUD.ShowPhoto.
        private static AlbumParts BuildAlbumSlot(RectTransform parent, UiTheme theme)
        {
            var root = Child(parent, "AlbumSlot", out var created);
            if (created)
            {
                Stretch(root);
                root.SetSiblingIndex(4);
            }

            var frame = Child(root, "Frame", out var frameCreated);
            var frameImage = GetOrAdd<Image>(frame.gameObject);
            var group = GetOrAdd<CanvasGroup>(frame.gameObject);
            if (frameCreated)
            {
                // Resting place: 64 px from the right and 40 px from the bottom, centred pivot so it can twist.
                Place(frame, new Vector2(1f, 0f), new Vector2(1f, 0f), Center, new Vector2(-64f - 170f, 40f + 100f), new Vector2(340f, 200f));
                frameImage.color = theme.cream;
                frameImage.raycastTarget = false;
                var outline = GetOrAdd<Outline>(frame.gameObject);
                outline.effectColor = theme.ink;
                outline.effectDistance = new Vector2(4f, -4f);
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var photo = Child(frame, "Photo", out var photoCreated);
            var raw = GetOrAdd<RawImage>(photo.gameObject);
            if (photoCreated)
            {
                Stretch(photo);
                photo.offsetMin = new Vector2(10f, 10f);
                photo.offsetMax = new Vector2(-10f, -10f);
                raw.raycastTarget = false;
            }

            if (frameCreated)
                frame.gameObject.SetActive(false);

            return new AlbumParts(frame, group, raw);
        }

        // Teammates (C8.5): top-right row inside the safe zone (64 px from the right, 40 from the top) with 2 fixed slots,
        // each an Icon (48x48) with the teammate's name under it. TeammatesHUD shows and fills the slots at runtime.
        private const int TeammateSlots = 2;
        private const float TeammateSlotWidth = 110f;
        private const float TeammateSlotGap = 16f;
        private const float TeammateIconSize = 48f;
        private const float TeammateNameHeight = 30f;

        private static void BuildTeammates(RectTransform parent, UiTheme theme, TMP_FontAsset font)
        {
            var top = Child(parent, "Teammates", out var created);
            if (created)
            {
                var width = TeammateSlots * TeammateSlotWidth + (TeammateSlots - 1) * TeammateSlotGap;
                Place(top, Vector2.one, Vector2.one, Vector2.one, new Vector2(-64f, -40f),
                    new Vector2(width, TeammateIconSize + TeammateNameHeight));
                top.SetSiblingIndex(3);
            }

            var roots = new Object[TeammateSlots];
            var icons = new Object[TeammateSlots];
            var names = new Object[TeammateSlots];
            for (var i = 0; i < TeammateSlots; i++)
            {
                var slot = Child(top, $"Slot{i + 1}", out var slotCreated);
                if (slotCreated)
                {
                    // Slot 1 sits on the right edge, slot 2 to its left, so one teammate alone hugs the corner.
                    var x = -i * (TeammateSlotWidth + TeammateSlotGap);
                    Place(slot, Vector2.one, Vector2.one, Vector2.one, new Vector2(x, 0f),
                        new Vector2(TeammateSlotWidth, TeammateIconSize + TeammateNameHeight));
                    slot.gameObject.SetActive(false);
                }

                var icon = Child(slot, "Icon", out var iconCreated);
                var image = GetOrAdd<Image>(icon.gameObject);
                if (iconCreated)
                {
                    Place(icon, TopCenter, TopCenter, Center, new Vector2(0f, -TeammateIconSize * 0.5f),
                        new Vector2(TeammateIconSize, TeammateIconSize));
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }

                var label = Text(slot, "Name", "Gnomo", theme.hintSize, theme.cream, TextAlignmentOptions.Top, font, false,
                    out var nameRect, out var nameCreated);
                if (nameCreated)
                {
                    Place(nameRect, BottomCenter, BottomCenter, new Vector2(0.5f, 0f), Vector2.zero,
                        new Vector2(TeammateSlotWidth, TeammateNameHeight));
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    ApplyTitleFont(label, theme, true);
                }

                roots[i] = slot;
                icons[i] = image;
                names[i] = label;
            }

            var hud = GetOrAdd<TeammatesHUD>(top.gameObject);
            var serialized = new SerializedObject(hud);
            SetArray(serialized.FindProperty("_slotRoots"), roots);
            SetArray(serialized.FindProperty("_icons"), icons);
            SetArray(serialized.FindProperty("_names"), names);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, Object[] values)
        {
            if (array == null)
            {
                Debug.LogError($"{LogPrefix} TeammatesHUD is missing a serialized array.");
                return;
            }

            if (array.arraySize != values.Length)
                array.arraySize = values.Length;

            for (var i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        // Lilita title font with the ink outline, only when the text is created (later editor tweaks survive).
        private static void ApplyTitleFont(TMP_Text text, UiTheme theme, bool outline)
        {
            if (theme.titleFont != null)
                text.font = theme.titleFont;
            if (outline && theme.titleOutline != null)
                text.fontSharedMaterial = theme.titleOutline;
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

        private static void BuildMicBadge(RectTransform parent, TMP_FontAsset font, out GameObject badge, out Image background,
            out TMP_Text label)
        {
            var rect = Child(parent, "MicBadge", out var created);
            badge = rect.gameObject;
            background = GetOrAdd<Image>(badge);
            if (created)
            {
                Place(rect, BottomLeft, BottomLeft, BottomLeft, new Vector2(64f, 40f), new Vector2(240f, 44f));
                background.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
                background.raycastTarget = false;
            }

            label = Text(rect, "Label", "Micro apagado · M", 20f, Color.white, TextAlignmentOptions.Center, font, false,
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
