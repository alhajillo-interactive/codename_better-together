// Editor-only builder for the settings panel (C3.4). Run it from the menu GnoMore/Construir ajustes:
// 1. creates (or completes) Prefabs/UI/SettingsPanel.prefab: a full-screen overlay canvas with sliders for master,
//    music, effects and voice volume, mouse sensitivity and camera bob, a microphone mode button and a close button,
//    with the SettingsPanel component wired (Esc toggles it, for use in a match);
// 2. opens the MainMenu scene, instantiates that prefab (Esc toggle off, a button opens it), adds an "Ajustes" button
//    (a copy of the credits button) and wires both on MainMenuController, then saves the scene.
// Idempotent: anything that already exists is kept as it is (so tweaks made in the editor survive); only references are wired again.
// To use it in a match, the orchestrator adds the prefab to CampBryan.
using System.IO;
using BetterTogether.UI;
using BetterTogether.Voice;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class SettingsBuilder
    {
        private const string LogPrefix = "[SettingsBuilder]";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/SettingsPanel.prefab";
        private const string MenuScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string RootName = "SettingsPanel";
        private const int UiLayer = 5;

        private static readonly Color Dim = new(0f, 0f, 0f, 0.65f);
        private static readonly Color WindowColor = new(0.09f, 0.1f, 0.12f, 0.96f);
        private static readonly Color TrackColor = new(0.25f, 0.27f, 0.3f, 1f);
        private static readonly Color FillColor = new(0.95f, 0.7f, 0.2f, 1f);
        private static readonly Color ButtonColor = new(0.2f, 0.22f, 0.26f, 1f);

        [MenuItem("GnoMore/Construir ajustes")]
        public static void Build()
        {
            BuildPrefab();
            BuildMenuScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Done. Add {PrefabPath} to the CampBryan scene so Esc opens it during a match.");
        }

        #region Prefab

        private static void BuildPrefab()
        {
            GameObject root;
            var loaded = File.Exists(PrefabPath);
            if (loaded)
            {
                root = PrefabUtility.LoadPrefabContents(PrefabPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)!);
                root = new GameObject(RootName, typeof(RectTransform));
                root.layer = UiLayer;
            }

            try
            {
                BuildContents(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (loaded)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }

            Debug.Log($"{LogPrefix} Prefab ready: {PrefabPath}");
        }

        private static void BuildContents(GameObject root)
        {
            var canvas = GetOrAdd<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 200;

            var scaler = GetOrAdd<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            GetOrAdd<GraphicRaycaster>(root);

            var panel = GetOrAdd<SettingsPanel>(root);
            var font = ResolveFont();

            var content = root.transform.Find("Content");
            if (content == null)
                content = BuildLayout(root.transform, font);

            var so = new SerializedObject(panel);
            Set(so, "_content", content.gameObject);
            Set(so, "_masterSlider", FindSlider(content, "Row_Master"));
            Set(so, "_musicSlider", FindSlider(content, "Row_Music"));
            Set(so, "_sfxSlider", FindSlider(content, "Row_Sfx"));
            Set(so, "_voiceSlider", FindSlider(content, "Row_Voice"));
            Set(so, "_sensitivitySlider", FindSlider(content, "Row_Sensitivity"));
            Set(so, "_bobSlider", FindSlider(content, "Row_Bob"));
            Set(so, "_micModeButton", content.Find("Window/MicMode")?.GetComponent<Button>());
            Set(so, "_micModeLabel", content.Find("Window/MicMode/Label")?.GetComponent<TMP_Text>());
            Set(so, "_talkKeyLabel", content.Find("Window/TalkKey")?.GetComponent<TMP_Text>());
            EnsureReduceFlashes(content, font);
            Set(so, "_reduceFlashesButton", content.Find("Window/ReduceFlashes")?.GetComponent<Button>());
            Set(so, "_reduceFlashesLabel", content.Find("Window/ReduceFlashes/Label")?.GetComponent<TMP_Text>());
            EnsureMapMode(content, font);
            Set(so, "_mapModeButton", content.Find("Window/MapMode")?.GetComponent<Button>());
            Set(so, "_mapModeLabel", content.Find("Window/MapMode/Label")?.GetComponent<TMP_Text>());
            Set(so, "_closeButton", content.Find("Window/Close")?.GetComponent<Button>());
            Set(so, "_voiceSettings", FindVoiceSettings());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform BuildLayout(Transform root, TMP_FontAsset font)
        {
            var content = NewRect("Content", root);
            Stretch(content);
            var dim = content.gameObject.AddComponent<Image>();
            dim.color = Dim; // also blocks clicks on whatever is behind

            var window = NewRect("Window", content);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(920f, 900f);
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

            CreateText("Title", window, font, "Ajustes", 56, TextAlignmentOptions.Center, 80f);
            CreateSliderRow(window, font, "Row_Master", "Volumen general", 0f, 1f);
            CreateSliderRow(window, font, "Row_Music", "Música", 0f, 1f);
            CreateSliderRow(window, font, "Row_Sfx", "Efectos", 0f, 1f);
            CreateSliderRow(window, font, "Row_Voice", "Voces", 0f, 1f);
            CreateSliderRow(window, font, "Row_Sensitivity", "Sensibilidad del ratón", 0.25f, 2.5f);
            CreateSliderRow(window, font, "Row_Bob", "Balanceo de cámara", 0f, 1f);
            CreateButton("MicMode", window, font, "Micrófono: abierto", 70f);
            CreateText("TalkKey", window, font, "", 32, TextAlignmentOptions.Center, 50f);
            CreateButton("ReduceFlashes", window, font, "Reducir destellos: no", 70f);
            CreateButton("MapMode", window, font, "Mapa: mantener", 70f);
            CreateButton("Close", window, font, "Cerrar", 70f);
            window.sizeDelta = new Vector2(window.sizeDelta.x, ReduceFlashesWindowHeight);
            return content;
        }

        private const float ReduceFlashesWindowHeight = 1000f;

        // C8.2: an existing prefab gets the "Reducir destellos" button (a copy of the microphone button style) above Close, and a
        // window tall enough for it. Nothing is written when the button already exists.
        private static void EnsureReduceFlashes(Transform content, TMP_FontAsset font)
        {
            var window = content.Find("Window") as RectTransform;
            if (window == null || window.Find("ReduceFlashes") != null)
                return;

            CreateButton("ReduceFlashes", window, font, "Reducir destellos: no", 70f);
            var created = window.Find("ReduceFlashes");
            var close = window.Find("Close");
            if (created != null && close != null)
                created.SetSiblingIndex(close.GetSiblingIndex());

            if (window.sizeDelta.y < ReduceFlashesWindowHeight)
                window.sizeDelta = new Vector2(window.sizeDelta.x, ReduceFlashesWindowHeight);
        }

        // C9.6: an existing prefab gets the "Mapa: mantener / alternar" button above Close. Nothing is written when it exists.
        private static void EnsureMapMode(Transform content, TMP_FontAsset font)
        {
            var window = content.Find("Window") as RectTransform;
            if (window == null || window.Find("MapMode") != null)
                return;

            CreateButton("MapMode", window, font, "Mapa: mantener", 70f);
            var created = window.Find("MapMode");
            var close = window.Find("Close");
            if (created != null && close != null)
                created.SetSiblingIndex(close.GetSiblingIndex());

            if (window.sizeDelta.y < ReduceFlashesWindowHeight)
                window.sizeDelta = new Vector2(window.sizeDelta.x, ReduceFlashesWindowHeight);
        }

        private static void CreateSliderRow(Transform parent, TMP_FontAsset font, string rowName, string label, float min, float max)
        {
            var row = NewRect(rowName, parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;

            var name = CreateText("Label", row, font, label, 34, TextAlignmentOptions.MidlineLeft, 60f);
            name.gameObject.GetComponent<LayoutElement>().preferredWidth = 330f;
            // Long labels ("Sensibilidad del ratón") shrink to fit their column instead of running into the slider.
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 34f;
            name.enableWordWrapping = false;

            var slider = CreateSlider("Slider", row, min, max);
            var sliderLayout = slider.gameObject.AddComponent<LayoutElement>();
            sliderLayout.flexibleWidth = 1f;
            sliderLayout.preferredHeight = 40f;

            var value = CreateText("Value", row, font, "100 %", 32, TextAlignmentOptions.MidlineRight, 60f);
            value.gameObject.GetComponent<LayoutElement>().preferredWidth = 110f;
        }

        private static Slider CreateSlider(string objectName, Transform parent, float min, float max)
        {
            var root = NewRect(objectName, parent);

            var background = NewRect("Background", root);
            background.anchorMin = new Vector2(0f, 0.35f);
            background.anchorMax = new Vector2(1f, 0.65f);
            background.offsetMin = background.offsetMax = Vector2.zero;
            background.gameObject.AddComponent<Image>().color = TrackColor;

            var fillArea = NewRect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = new Vector2(10f, 0f);
            fillArea.offsetMax = new Vector2(-10f, 0f);
            var fill = NewRect("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = FillColor;

            var handleArea = NewRect("Handle Slide Area", root);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = NewRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(28f, 0f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = max;
            return slider;
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
            label.textWrappingMode = TextWrappingModes.NoWrap;
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        #endregion

        #region Menu scene

        private static void BuildMenuScene()
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

            var panel = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = RootName;
                panel = instance.GetComponent<SettingsPanel>();
            }

            var panelSo = new SerializedObject(panel);
            Set(panelSo, "_toggleWithEscape", false);
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            var button = EnsureMenuButton(controllerSo);
            Set(controllerSo, "settingsBTN", button);
            Set(controllerSo, "settingsPanel", panel);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{LogPrefix} MainMenu wired: panel and 'Ajustes' button.");
        }

        // Reuses a button called SettingsBTN next to the credits button, or copies the credits button.
        private static Button EnsureMenuButton(SerializedObject controllerSo)
        {
            var credits = controllerSo.FindProperty("creditsBTN").objectReferenceValue as Button;
            if (credits == null)
            {
                Debug.LogWarning($"{LogPrefix} creditsBTN is not assigned; add the settings button by hand.");
                return null;
            }

            var parent = credits.transform.parent;
            var existing = parent.Find("SettingsBTN");
            if (existing != null)
                return existing.GetComponent<Button>();

            var copy = Object.Instantiate(credits.gameObject, parent);
            copy.name = "SettingsBTN";
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
                tmp.text = "Ajustes";
            else if (copy.GetComponentInChildren<Text>(true) is { } legacy)
                legacy.text = "Ajustes";

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

        private static Slider FindSlider(Transform content, string row) =>
            content.Find($"Window/{row}/Slider")?.GetComponent<Slider>();

        private static TMP_FontAsset ResolveFont()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset Black_Han_Sans"))
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null)
                    return font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static VoiceChatSettings FindVoiceSettings()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:VoiceChatSettings"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<VoiceChatSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    return asset;
            }

            Debug.LogWarning($"{LogPrefix} No VoiceChatSettings asset found; the microphone button will do nothing.");
            return null;
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
                Debug.LogWarning($"{LogPrefix} Field not found: {field}");
            else
                property.objectReferenceValue = value;
        }

        private static void Set(SerializedObject so, string field, bool value)
        {
            var property = so.FindProperty(field);
            if (property != null)
                property.boolValue = value;
        }

        #endregion
    }
}
