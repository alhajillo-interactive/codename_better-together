// Editor-only builder for the role card and the persistent controls list (C3.1). Run it from the menu
// GnoMore/Construir tarjeta de rol y controles: it opens Prefabs/UI/MatchHUD.prefab, creates (or reuses) the uGUI/TMP
// children "RoleCard" (centred) and "ControlsHint" (bottom right), adds RoleCard and ControlsHint on the prefab root
// and wires their references. Idempotent: elements that already exist are kept as they are (so tweaks made in the
// editor survive); only the references are wired again.
using System.IO;
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class OnboardingBuilder
    {
        private const string LogPrefix = "[OnboardingBuilder]";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/MatchHUD.prefab";
        private const int UiLayer = 5;
        private const int LineCount = 6;

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Vector2 BottomRight = new(1f, 0f);

        [MenuItem("GnoMore/Construir tarjeta de rol y controles")]
        public static void Build()
        {
            if (!File.Exists(PrefabPath))
            {
                Debug.LogError($"{LogPrefix} Prefab not found: {PrefabPath}");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BuildContents(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Role card and controls hint built in {PrefabPath}.");
        }

        private static void BuildContents(GameObject root)
        {
            var rootRect = (RectTransform)root.transform;
            var font = FindFont(root);

            BuildCard(rootRect, font, out var card, out var cardGroup, out var title, out var objective, out var controls);
            BuildHint(rootRect, font, out var hintRoot, out var hintGroup, out var lines);

            Wire(GetOrAdd<RoleCard>(root),
                ("_card", card),
                ("_group", cardGroup),
                ("_title", title),
                ("_objective", objective),
                ("_controls", controls));

            var hint = GetOrAdd<ControlsHint>(root);
            Wire(hint, ("_root", hintRoot), ("_group", hintGroup));
            var serialized = new SerializedObject(hint);
            var array = serialized.FindProperty("_lines");
            array.arraySize = lines.Length;
            for (var i = 0; i < lines.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = lines[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Draw order: above the gameplay elements, below the end screen and the flash.
            var endScreen = rootRect.Find("EndScreen");
            var index = endScreen != null ? endScreen.GetSiblingIndex() : rootRect.childCount;
            hintRoot.transform.SetSiblingIndex(index);
            card.transform.SetSiblingIndex(index + 1);
            if (endScreen != null)
                endScreen.SetAsLastSibling();

            var flash = rootRect.Find("Flash");
            if (flash != null)
                flash.SetAsLastSibling();
        }

        private static TMP_FontAsset FindFont(GameObject root)
        {
            var existing = root.GetComponentInChildren<TMP_Text>(true);
            return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        private static void BuildCard(RectTransform parent, TMP_FontAsset font, out GameObject card, out CanvasGroup group,
            out TMP_Text title, out TMP_Text objective, out TMP_Text controls)
        {
            var rect = Child(parent, "RoleCard", out var created);
            card = rect.gameObject;
            var background = GetOrAdd<Image>(card);
            group = GetOrAdd<CanvasGroup>(card);
            if (created)
            {
                Place(rect, Center, Center, Center, new Vector2(0f, 60f), new Vector2(1100f, 420f));
                background.color = new Color(0f, 0f, 0f, 0.7f);
                background.raycastTarget = false;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            title = Text(rect, "Title", "Eres un GNOMO", 84f, Color.white, TextAlignmentOptions.Center, font, out var titleRect, out var titleCreated);
            if (titleCreated)
                Place(titleRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(-60f, 110f));

            objective = Text(rect, "Objective", "Roba, esconde y apaga la fogata", 44f, new Color(1f, 0.85f, 0.3f),
                TextAlignmentOptions.Center, font, out var objectiveRect, out var objectiveCreated);
            if (objectiveCreated)
                Place(objectiveRect, Center, Center, Center, new Vector2(0f, 20f), new Vector2(1000f, 70f));

            controls = Text(rect, "Controls", "[E] usar", 32f, Color.white, TextAlignmentOptions.Center, font,
                out var controlsRect, out var controlsCreated);
            if (controlsCreated)
            {
                Place(controlsRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(-60f, 110f));
                controls.textWrappingMode = TextWrappingModes.Normal;
            }

            if (created)
                card.SetActive(false);
        }

        private static void BuildHint(RectTransform parent, TMP_FontAsset font, out GameObject hintRoot, out CanvasGroup group,
            out TMP_Text[] lines)
        {
            var rect = Child(parent, "ControlsHint", out var created);
            hintRoot = rect.gameObject;
            group = GetOrAdd<CanvasGroup>(hintRoot);
            if (created)
            {
                Place(rect, BottomRight, BottomRight, BottomRight, new Vector2(-24f, 24f), new Vector2(380f, 0f));
                group.interactable = false;
                group.blocksRaycasts = false;

                var layout = GetOrAdd<VerticalLayoutGroup>(hintRoot);
                layout.padding = new RectOffset(12, 12, 8, 8);
                layout.spacing = 2f;
                layout.childAlignment = TextAnchor.LowerRight;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                var fitter = GetOrAdd<ContentSizeFitter>(hintRoot);
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            lines = new TMP_Text[LineCount];
            for (var i = 0; i < LineCount; i++)
            {
                lines[i] = Text(rect, $"Line{i}", "[E] usar", 26f, Color.white, TextAlignmentOptions.MidlineRight, font,
                    out _, out var lineCreated);
                if (lineCreated)
                    lines[i].gameObject.SetActive(false);
            }

            if (created)
                hintRoot.SetActive(false);
        }

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
            TextAlignmentOptions alignment, TMP_FontAsset font, out RectTransform rect, out bool created)
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
            text.textWrappingMode = TextWrappingModes.NoWrap;
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
