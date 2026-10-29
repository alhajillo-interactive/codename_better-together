// Shared by GnoMore/Construir HUD de juego (GameHudBuilder) and GnoMore/Aplicar estilo de UI (UiThemeApplier): the parts of
// the MatchHUD prefab that C6.2a makes elastic or adds. All of it is idempotent (a value that already matches is not written)
// and returns how many writes it made, so the callers know whether the prefab must be saved.
//  - Objectives plate: "HeaderBG" holds the "Title" text and an ElasticPlate, so the plate grows with the milestone title.
//  - Objective lines: auto-fit text (min 22 px), room on the right and a "Stamp" child Image per line (check / cross stamp).
//  - Toast pill: "AnnouncerPill" (event-coloured border, night80 fill) becomes the parent of the "Announcer" text.
//  - Microphone badge: placed inside the safe zone (64 px from the left, 40 px from the bottom).
// Never touches scenes; the prefab is edited through PrefabUtility contents by the callers.
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    internal static class HudPlateTools
    {
        private const int UiLayer = 5;
        private const float HeaderLeft = 11f;
        private const float HeaderCenterY = 45f;
        private const float LineStampSize = 34f;
        private const float LineRightRoom = 72f;
        private const float PillBorder = 3f;
        private const float ToastFontSize = 48f;
        private static readonly string[] LineNames = { "Line1", "Line2", "Line3", "Line4" };

        /// <summary>Applies every C6.2a prefab change to the MatchHUD contents; returns the number of writes.</summary>
        public static int Ensure(GameObject root, UiTheme theme)
        {
            if (root == null || theme == null)
                return 0;

            var rect = (RectTransform)root.transform;
            var changes = 0;
            var objectives = rect.Find("Objectives");
            if (objectives != null)
            {
                changes += EnsureObjectivesPlate(objectives, theme);
                changes += EnsureLineStamps(objectives, theme);
            }

            changes += EnsureAnnouncerPill(rect, theme);
            changes += EnsureMicBadge(rect);
            return changes;
        }

        // ---------------------------------------------------------------- Objectives plate

        private static int EnsureObjectivesPlate(Transform objectives, UiTheme theme)
        {
            var header = objectives.Find("HeaderBG") as RectTransform;
            var title = FindText(objectives, "Title", header);
            if (header == null || title == null)
                return 0;

            var changes = 0;

            // Plate hangs from the top-left corner of the note, vertically centred on the old title line.
            changes += SetRect(header, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(HeaderLeft, HeaderCenterY));
            changes += SetImage(header.GetComponent<Image>(), theme.header, Color.white);

            if (title.transform.parent != header)
            {
                title.transform.SetParent(header, false);
                changes++;
            }

            var titleRect = title.rectTransform;
            if (titleRect.anchorMin != Vector2.zero || titleRect.anchorMax != Vector2.one)
            {
                titleRect.anchorMin = Vector2.zero;
                titleRect.anchorMax = Vector2.one;
                titleRect.pivot = new Vector2(0.5f, 0.5f);
                titleRect.offsetMin = new Vector2(24f, 0f);
                titleRect.offsetMax = new Vector2(-24f, 0f);
                changes++;
            }

            changes += SetText(title, theme.cream, theme.titleFont, theme.titleOutline, TextAlignmentOptions.Center);
            if (title.textWrappingMode != TextWrappingModes.NoWrap)
            {
                title.textWrappingMode = TextWrappingModes.NoWrap;
                changes++;
            }

            var plate = header.GetComponent<ElasticPlate>();
            var created = plate == null;
            if (created)
            {
                plate = header.gameObject.AddComponent<ElasticPlate>();
                changes++;
            }

            changes += Wire(plate, ("_plate", header), ("_text", title));
            if (created)
            {
                var so = new SerializedObject(plate);
                so.FindProperty("_paddingX").floatValue = 24f;
                so.FindProperty("_paddingY").floatValue = 8f;
                so.FindProperty("_minWidth").floatValue = 160f;
                so.FindProperty("_maxWidth").floatValue = 520f;
                so.FindProperty("_fitHeight").boolValue = false;
                so.FindProperty("_minFontSize").floatValue = 24f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return changes;
        }

        // ---------------------------------------------------------------- Objective lines and stamps

        private static int EnsureLineStamps(Transform objectives, UiTheme theme)
        {
            var changes = 0;
            foreach (var name in LineNames)
            {
                var line = objectives.Find(name);
                if (line == null || !line.TryGetComponent<TMP_Text>(out var text))
                    continue;

                // Long lines shrink (down to 22 px) instead of leaving the paper.
                if (!text.enableAutoSizing)
                {
                    var size = text.fontSize;
                    text.enableAutoSizing = true;
                    text.fontSizeMax = size;
                    text.fontSizeMin = 22f;
                    text.fontSize = size;
                    changes++;
                }

                // The stamp sits at the start of the line like a checklist box; the right margin keeps the text on the paper.
                var margin = text.margin;
                if (margin.x != LineStampSize + 8f || margin.z != LineRightRoom)
                {
                    margin.x = LineStampSize + 8f;
                    margin.z = LineRightRoom;
                    text.margin = margin;
                    changes++;
                }

                var stamp = line.Find("Stamp") as RectTransform;
                if (stamp == null)
                {
                    var go = new GameObject("Stamp", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { layer = UiLayer };
                    stamp = (RectTransform)go.transform;
                    stamp.SetParent(line, false);
                    changes++;
                }

                changes += SetRect(stamp, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                    new Vector2(LineStampSize, LineStampSize));

                var image = stamp.GetComponent<Image>();
                changes += SetImage(image, theme.stamp, Color.white);
                if (image.raycastTarget)
                {
                    image.raycastTarget = false;
                    changes++;
                }

                // Starts hidden; MatchHUD shows it when the line is done or failed.
                if (image.enabled)
                {
                    image.enabled = false;
                    changes++;
                }
            }

            return changes;
        }

        // ---------------------------------------------------------------- Toast pill

        private static int EnsureAnnouncerPill(RectTransform root, UiTheme theme)
        {
            var pill = root.Find("AnnouncerPill") as RectTransform;
            var announcer = (pill != null ? pill.Find("Announcer") : root.Find("Announcer")) as RectTransform;
            if (announcer == null || !announcer.TryGetComponent<TMP_Text>(out var text))
                return 0;

            var changes = 0;
            if (pill == null)
            {
                var go = new GameObject("AnnouncerPill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup))
                    { layer = UiLayer };
                pill = (RectTransform)go.transform;
                pill.SetParent(root, false);
                pill.SetSiblingIndex(announcer.GetSiblingIndex());

                // Same place as the old centred announcer; the plate component sizes it from the text at runtime.
                pill.anchorMin = announcer.anchorMin;
                pill.anchorMax = announcer.anchorMax;
                pill.pivot = announcer.pivot;
                pill.anchoredPosition = announcer.anchoredPosition;
                pill.sizeDelta = new Vector2(400f, 96f);
                changes++;
            }

            var border = pill.GetComponent<Image>();
            changes += SetImage(border, theme.pill, theme.ember);
            if (border.raycastTarget)
            {
                border.raycastTarget = false;
                changes++;
            }

            var group = pill.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = pill.gameObject.AddComponent<CanvasGroup>();
                changes++;
            }

            if (group.interactable || group.blocksRaycasts)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
                changes++;
            }

            // Dark fill inside the coloured border.
            var fill = pill.Find("Fill") as RectTransform;
            if (fill == null)
            {
                var go = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { layer = UiLayer };
                fill = (RectTransform)go.transform;
                fill.SetParent(pill, false);
                fill.SetAsFirstSibling();
                changes++;
            }

            if (fill.anchorMin != Vector2.zero || fill.anchorMax != Vector2.one
                || fill.offsetMin != new Vector2(PillBorder, PillBorder) || fill.offsetMax != new Vector2(-PillBorder, -PillBorder))
            {
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.pivot = new Vector2(0.5f, 0.5f);
                fill.offsetMin = new Vector2(PillBorder, PillBorder);
                fill.offsetMax = new Vector2(-PillBorder, -PillBorder);
                changes++;
            }

            var fillImage = fill.GetComponent<Image>();
            // Opaque fill: a translucent one would mix with the event-coloured border drawn underneath it.
            var night = theme.night80;
            night.a = 1f;
            changes += SetImage(fillImage, theme.pill, night);
            if (fillImage.raycastTarget)
            {
                fillImage.raycastTarget = false;
                changes++;
            }

            if (announcer.parent != pill)
            {
                announcer.SetParent(pill, false);
                changes++;
            }

            if (announcer.GetSiblingIndex() != pill.childCount - 1)
            {
                announcer.SetAsLastSibling();
                changes++;
            }

            // The toast text is always cream; the event colour lives on the border.
            changes += SetText(text, theme.cream, text.font, text.fontSharedMaterial, TextAlignmentOptions.Center);

            // Toasts live under the clock (plan §6), away from the objectives note and the prompt; 48 px reads at a glance and the pill stays clear of the objectives note.
            changes += SetRect(pill, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f));
            if (!Mathf.Approximately(text.fontSize, ToastFontSize) || text.enableAutoSizing)
            {
                text.enableAutoSizing = false;
                text.fontSize = ToastFontSize;
                changes++;
            }

            var plate = pill.GetComponent<ElasticPlate>();
            var created = plate == null;
            if (created)
            {
                plate = pill.gameObject.AddComponent<ElasticPlate>();
                changes++;
            }

            changes += Wire(plate, ("_plate", pill), ("_text", text));
            if (created)
            {
                var so = new SerializedObject(plate);
                so.FindProperty("_paddingX").floatValue = 40f;
                so.FindProperty("_paddingY").floatValue = 14f;
                so.FindProperty("_minWidth").floatValue = 160f;
                so.FindProperty("_maxWidth").floatValue = 860f;
                so.FindProperty("_fitHeight").boolValue = true;
                so.FindProperty("_minHeight").floatValue = 72f;
                so.FindProperty("_minFontSize").floatValue = 30f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return changes;
        }

        // ---------------------------------------------------------------- Microphone badge

        private static int EnsureMicBadge(RectTransform root)
        {
            var badge = root.Find("MicBadge") as RectTransform;
            if (badge == null)
                return 0;

            // Inside the safe zone: at least 64 px from the left edge and 40 px from the bottom.
            return SetRect(badge, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(64f, 40f), new Vector2(240f, 44f));
        }

        // ---------------------------------------------------------------- Write helpers (write only when different)

        private static TMP_Text FindText(Transform objectives, string name, Transform header)
        {
            var found = objectives.Find(name);
            if (found == null && header != null)
                found = header.Find(name);

            return found != null ? found.GetComponent<TMP_Text>() : null;
        }

        private static int SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position)
        {
            if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax && rect.pivot == pivot && rect.anchoredPosition == position)
                return 0;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            return 1;
        }

        private static int SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            if (rect.anchorMin == anchorMin && rect.anchorMax == anchorMax && rect.pivot == pivot
                && rect.anchoredPosition == position && rect.sizeDelta == size)
                return 0;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return 1;
        }

        private static int SetImage(Image image, Sprite sprite, Color color)
        {
            if (image == null)
                return 0;

            var type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            if (image.sprite == sprite && image.type == type && image.color == color && !image.preserveAspect)
                return 0;

            image.sprite = sprite;
            image.type = type;
            image.color = color;
            image.preserveAspect = false;
            return 1;
        }

        private static int SetText(TMP_Text text, Color color, TMP_FontAsset font, Material material, TextAlignmentOptions alignment)
        {
            var changes = 0;
            if (text.color != color)
            {
                text.color = color;
                changes++;
            }

            if (font != null && text.font != font)
            {
                text.font = font;
                changes++;
            }

            if (material != null && text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
                changes++;
            }

            if (text.alignment != alignment)
            {
                text.alignment = alignment;
                changes++;
            }

            return changes;
        }

        private static int Wire(Component target, params (string field, Object value)[] references)
        {
            var so = new SerializedObject(target);
            var changes = 0;
            foreach (var (field, value) in references)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"[HudPlateTools] {target.GetType().Name} has no serialized field '{field}'.");
                    continue;
                }

                if (property.objectReferenceValue == value)
                    continue;

                property.objectReferenceValue = value;
                changes++;
            }

            if (changes > 0)
                so.ApplyModifiedPropertiesWithoutUndo();
            return changes;
        }
    }
}
