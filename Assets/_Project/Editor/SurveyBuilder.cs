// Editor-only builder for the post-match survey (C3.3). Run it from the menu GnoMore/Construir encuesta: it creates (or
// reuses) a "SurveyCanvas" in the open scene with the panel, the question buttons, the free-text field and the send/skip
// buttons, adds PostMatchSurvey to it and wires the references. It is idempotent: existing elements are kept as they are
// (so tweaks made in the editor survive); only the references are wired again. It also adds an EventSystem when the
// scene has none. Save the scene afterwards.
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class SurveyBuilder
    {
        private const string LogPrefix = "[SurveyBuilder]";
        private const string CanvasName = "SurveyCanvas";
        private const int UiLayer = 5;

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.92f);
        private static readonly Color ButtonColor = Color.white;

        [MenuItem("GnoMore/Construir encuesta")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"{LogPrefix} No scene is open.");
                return;
            }

            var font = TMP_Settings.defaultFontAsset;
            var canvasRect = EnsureCanvas();
            var survey = GetOrAdd<PostMatchSurvey>(canvasRect.gameObject);

            var panel = Child(canvasRect, "Panel", out var panelCreated);
            if (panelCreated)
            {
                Place(panel, Center, Center, Center, Vector2.zero, new Vector2(760f, 700f));
                GetOrAdd<Image>(panel.gameObject).color = PanelColor;
            }

            Text(panel, "Title", "Encuesta rápida (opcional)", 34f, TextAlignmentOptions.Center, font, new Vector2(0f, 310f), new Vector2(700f, 50f));

            Text(panel, "FunLabel", "¿Qué tan divertido fue? (1 = nada, 5 = mucho)", 24f, TextAlignmentOptions.Left, font, new Vector2(0f, 245f), new Vector2(700f, 36f));
            var fun = new Button[5];
            for (var i = 0; i < fun.Length; i++)
                fun[i] = MakeButton(panel, "Fun" + (i + 1), (i + 1).ToString(), font, new Vector2(-240f + i * 120f, 190f), new Vector2(100f, 56f));

            Text(panel, "DizzyLabel", "¿Te mareaste?", 24f, TextAlignmentOptions.Left, font, new Vector2(0f, 120f), new Vector2(700f, 36f));
            var dizzyYes = MakeButton(panel, "DizzyYes", "Sí", font, new Vector2(-90f, 65f), new Vector2(160f, 56f));
            var dizzyNo = MakeButton(panel, "DizzyNo", "No", font, new Vector2(90f, 65f), new Vector2(160f, 56f));

            Text(panel, "UnderstoodLabel", "¿Entendiste tu objetivo desde el principio?", 24f, TextAlignmentOptions.Left, font, new Vector2(0f, -5f), new Vector2(700f, 36f));
            var understoodYes = MakeButton(panel, "UnderstoodYes", "Sí", font, new Vector2(-90f, -60f), new Vector2(160f, 56f));
            var understoodNo = MakeButton(panel, "UnderstoodNo", "No", font, new Vector2(90f, -60f), new Vector2(160f, 56f));

            Text(panel, "MomentLabel", "¿Qué momento recuerdas? (máx. 140 caracteres)", 24f, TextAlignmentOptions.Left, font, new Vector2(0f, -130f), new Vector2(700f, 36f));
            var moment = MakeInput(panel, "Moment", font, new Vector2(0f, -215f), new Vector2(700f, 100f));

            var send = MakeButton(panel, "Send", "Enviar", font, new Vector2(-130f, -310f), new Vector2(220f, 60f));
            var skip = MakeButton(panel, "Skip", "Omitir", font, new Vector2(130f, -310f), new Vector2(220f, 60f));

            var serialized = new SerializedObject(survey);
            SetRef(serialized, "_panel", panel.gameObject);
            SetRef(serialized, "_dizzyYes", dizzyYes);
            SetRef(serialized, "_dizzyNo", dizzyNo);
            SetRef(serialized, "_understoodYes", understoodYes);
            SetRef(serialized, "_understoodNo", understoodNo);
            SetRef(serialized, "_moment", moment);
            SetRef(serialized, "_send", send);
            SetRef(serialized, "_skip", skip);
            var funProperty = serialized.FindProperty("_funButtons");
            funProperty.arraySize = fun.Length;
            for (var i = 0; i < fun.Length; i++)
                funProperty.GetArrayElementAtIndex(i).objectReferenceValue = fun[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The survey script hides the panel at runtime; keep it visible in the editor so it can be adjusted.
            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{LogPrefix} Survey built in scene '{scene.name}'. Save the scene (the panel is hidden at runtime until the end screen).");
        }

        private static RectTransform EnsureCanvas()
        {
            var existing = GameObject.Find(CanvasName);
            GameObject go;
            if (existing != null)
            {
                go = existing;
            }
            else
            {
                go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = UiLayer };
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                Undo.RegisterCreatedObjectUndo(go, "Build survey");
            }

            return (RectTransform)go.transform;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build survey");
        }

        #region Helpers

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

        private static void Text(Transform parent, string name, string content, float size, TextAlignmentOptions alignment,
            TMP_FontAsset font, Vector2 position, Vector2 boxSize)
        {
            var rect = Child(parent, name, out var created);
            var text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
            if (!created)
                return;

            Place(rect, Center, Center, Center, position, boxSize);
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static Button MakeButton(Transform parent, string name, string label, TMP_FontAsset font, Vector2 position, Vector2 boxSize)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            var button = GetOrAdd<Button>(rect.gameObject);
            if (!created)
                return button;

            Place(rect, Center, Center, Center, position, boxSize);
            image.color = ButtonColor;
            button.targetGraphic = image;

            var labelRect = Child(rect, "Label", out _);
            Stretch(labelRect);
            var text = GetOrAdd<TextMeshProUGUI>(labelRect.gameObject);
            text.font = font;
            text.text = label;
            text.fontSize = 26f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            text.raycastTarget = false;
            return button;
        }

        private static TMP_InputField MakeInput(Transform parent, string name, TMP_FontAsset font, Vector2 position, Vector2 boxSize)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            var input = GetOrAdd<TMP_InputField>(rect.gameObject);
            if (!created)
                return input;

            Place(rect, Center, Center, Center, position, boxSize);
            image.color = new Color(1f, 1f, 1f, 0.95f);

            var viewport = Child(rect, "Text Area", out _);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(12f, 8f);
            viewport.offsetMax = new Vector2(-12f, -8f);
            GetOrAdd<RectMask2D>(viewport.gameObject);

            var textRect = Child(viewport, "Text", out _);
            Stretch(textRect);
            var text = GetOrAdd<TextMeshProUGUI>(textRect.gameObject);
            text.font = font;
            text.fontSize = 24f;
            text.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;

            var placeholderRect = Child(viewport, "Placeholder", out _);
            Stretch(placeholderRect);
            var placeholder = GetOrAdd<TextMeshProUGUI>(placeholderRect.gameObject);
            placeholder.font = font;
            placeholder.text = "Escribe aquí…";
            placeholder.fontSize = 24f;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.1f, 0.1f, 0.1f, 0.4f);
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            placeholder.raycastTarget = false;

            input.targetGraphic = image;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.characterLimit = 140;
            return input;
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

        private static void SetRef(SerializedObject serialized, string field, Object value)
        {
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"{LogPrefix} PostMatchSurvey has no serialized field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
        }

        #endregion
    }
}
