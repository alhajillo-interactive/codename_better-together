// Editor-only builder for the public games panel (C4.3). Run it from the menu GnoMore/Construir partidas públicas:
// opens the MainMenu scene, creates (or completes) a "PublicLobbyPanel" next to the main menu buttons (uGUI + TMP:
// title, session list with a row template, empty label, status label, Crear pública / Unión rápida / Volver),
// adds a "Partidas públicas" button (a copy of the credits button) and wires both on MainMenuController, then saves the scene.
// Idempotent: anything that already exists is kept as it is (so tweaks made in the editor survive); only references are wired again.
using System.IO;
using BetterTogether.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class PublicLobbyBuilder
    {
        private const string LogPrefix = "[PublicLobbyBuilder]";
        private const string MenuScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string RootName = "PublicLobbyPanel";
        private const string ButtonName = "PublicBTN";
        private const int UiLayer = 5;

        private static readonly Color Dim = new(0f, 0f, 0f, 0.65f);
        private static readonly Color WindowColor = new(0.09f, 0.1f, 0.12f, 0.96f);
        private static readonly Color ButtonColor = new(0.2f, 0.22f, 0.26f, 1f);
        private static readonly Color RowColor = new(0.16f, 0.18f, 0.21f, 1f);

        [MenuItem("GnoMore/Construir partidas públicas")]
        public static void Build()
        {
            if (!File.Exists(MenuScenePath))
            {
                Debug.LogError($"{LogPrefix} Scene not found: {MenuScenePath}");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogError($"{LogPrefix} Cancelled: save the open scenes first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            if (controller == null)
            {
                Debug.LogError($"{LogPrefix} No MainMenuController in {MenuScenePath}.");
                return;
            }

            var controllerSo = new SerializedObject(controller);
            var mainMenu = controllerSo.FindProperty("mainMenu").objectReferenceValue as GameObject;
            var credits = controllerSo.FindProperty("creditsBTN").objectReferenceValue as Button;
            if (mainMenu == null || credits == null)
            {
                Debug.LogError($"{LogPrefix} MainMenuController needs mainMenu and creditsBTN assigned.");
                return;
            }

            var font = ResolveFont(credits);
            var canvasTransform = mainMenu.transform.parent;

            var panel = EnsurePanel(canvasTransform, font);
            var button = EnsureMenuButton(credits);

            Set(controllerSo, "publicBTN", button);
            Set(controllerSo, "publicPanel", panel);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{LogPrefix} MainMenu wired: public games panel and 'Partidas públicas' button.");
        }

        #region Panel

        private static PublicLobbyPanel EnsurePanel(Transform parent, TMP_FontAsset font)
        {
            var existing = parent.Find(RootName);
            var root = existing != null ? (RectTransform)existing : NewRect(RootName, parent);
            if (existing == null)
                Stretch(root);

            var panel = GetOrAdd<PublicLobbyPanel>(root.gameObject);

            var content = root.Find("Content");
            if (content == null)
                content = BuildLayout(root, font);

            var so = new SerializedObject(panel);
            Set(so, "_content", content.gameObject);
            Set(so, "_listRoot", content.Find("Window/List") as RectTransform);
            Set(so, "_rowTemplate", content.Find("Window/List/RowTemplate")?.gameObject);
            Set(so, "_emptyLabel", content.Find("Window/Empty")?.GetComponent<TMP_Text>());
            Set(so, "_status", content.Find("Window/Status")?.GetComponent<TMP_Text>());
            Set(so, "_createButton", content.Find("Window/Buttons/Create")?.GetComponent<Button>());
            Set(so, "_quickJoinButton", content.Find("Window/Buttons/QuickJoin")?.GetComponent<Button>());
            Set(so, "_backButton", content.Find("Window/Buttons/Back")?.GetComponent<Button>());
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        private static Transform BuildLayout(Transform root, TMP_FontAsset font)
        {
            var content = NewRect("Content", root);
            Stretch(content);
            content.gameObject.AddComponent<Image>().color = Dim; // also blocks clicks on whatever is behind

            var window = NewRect("Window", content);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(1100f, 860f);
            window.anchoredPosition = Vector2.zero;
            window.gameObject.AddComponent<Image>().color = WindowColor;
            var layout = window.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 30, 30);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText("Title", window, font, "Partidas públicas", 56, TextAlignmentOptions.Center, 80f);

            var list = NewRect("List", window);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 10f;
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            var listElement = list.gameObject.AddComponent<LayoutElement>();
            listElement.flexibleHeight = 1f;
            listElement.minHeight = 380f;
            BuildRowTemplate(list, font);

            var empty = CreateText("Empty", window, font, "No hay partidas abiertas. Crea una o prueba la unión rápida.", 32,
                TextAlignmentOptions.Center, 70f);
            empty.color = new Color(1f, 1f, 1f, 0.7f);
            CreateText("Status", window, font, "", 34, TextAlignmentOptions.Center, 60f);

            var buttons = NewRect("Buttons", window);
            var buttonsLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = 20f;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = true;
            buttonsLayout.childForceExpandHeight = false;
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
            CreateButton("Create", buttons, font, "Crear pública", 80f);
            CreateButton("QuickJoin", buttons, font, "Unión rápida", 80f);
            CreateButton("Back", buttons, font, "Volver", 80f);
            return content;
        }

        private static void BuildRowTemplate(Transform list, TMP_FontAsset font)
        {
            var row = NewRect("RowTemplate", list);
            row.gameObject.AddComponent<Image>().color = RowColor;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 8, 8);
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;

            var name = CreateText("Name", row, font, "Campamento", 34, TextAlignmentOptions.MidlineLeft, 60f);
            name.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var players = CreateText("Players", row, font, "1/8", 34, TextAlignmentOptions.Center, 60f);
            players.GetComponent<LayoutElement>().preferredWidth = 110f;
            var state = CreateText("State", row, font, "En sala", 32, TextAlignmentOptions.Center, 60f);
            state.GetComponent<LayoutElement>().preferredWidth = 210f;

            CreateButton("Join", row, font, "Unirse", 60f);
            row.Find("Join").GetComponent<LayoutElement>().preferredWidth = 180f;
            row.gameObject.SetActive(false);
        }

        #endregion

        #region Menu button

        // Reuses a button called PublicBTN next to the credits button, or copies the credits button.
        private static Button EnsureMenuButton(Button credits)
        {
            var parent = credits.transform.parent;
            var existing = parent.Find(ButtonName);
            if (existing != null)
                return existing.GetComponent<Button>();

            var copy = Object.Instantiate(credits.gameObject, parent);
            copy.name = ButtonName;
            copy.transform.SetSiblingIndex(credits.transform.GetSiblingIndex());

            var button = copy.GetComponent<Button>();
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls != null)
            {
                calls.serializedObject.Update();
                calls.arraySize = 0;
                calls.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var tmp = copy.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                tmp.text = "Partidas públicas";
            else if (copy.GetComponentInChildren<Text>(true) is { } legacy)
                legacy.text = "Partidas públicas";

            return button;
        }

        #endregion

        #region Helpers

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static RectTransform NewRect(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform)) { layer = UiLayer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void CreateButton(string objectName, Transform parent, TMP_FontAsset font, string label, float height)
        {
            var rect = NewRect(objectName, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

            var text = CreateText("Label", rect, font, label, 34, TextAlignmentOptions.Center, height);
            Stretch(text.rectTransform);
            Object.DestroyImmediate(text.gameObject.GetComponent<LayoutElement>());
            text.raycastTarget = false;
        }

        private static TMP_Text CreateText(string objectName, Transform parent, TMP_FontAsset font, string text, float size,
            TextAlignmentOptions alignment, float height)
        {
            var rect = NewRect(objectName, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        // The menu's own font (from the credits button) so the panel matches; falls back to the project's TMP default.
        private static TMP_FontAsset ResolveFont(Button reference)
        {
            var text = reference.GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.font != null)
                return text.font;

            return TMP_Settings.defaultFontAsset;
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
                Debug.LogWarning($"{LogPrefix} Field not found: {field}");
            else
                property.objectReferenceValue = value;
        }

        #endregion
    }
}
