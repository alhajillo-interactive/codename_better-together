// Editor-only builder for the lobby screen (C3.2). Run it from the menu GnoMore/Construir lobby: it opens
// Prefabs/UI/StartMatch_Canvas.prefab, creates (or reuses) the uGUI/TMP children under Panel/Lobby (room code and copy
// button, players header, player rows, camper info, status line, ready button), moves the existing start button next to
// the ready button and wires everything into StartMatchUI. Idempotent: elements that already exist keep their layout
// (so tweaks made in the editor survive); only the references are wired again.
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class LobbyBuilder
    {
        private const string LogPrefix = "[LobbyBuilder]";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/StartMatch_Canvas.prefab";
        private const int UiLayer = 5;
        private const int RowCount = 10;
        private const float RowHeight = 44f;

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Vector2 TopCenter = new(0.5f, 1f);

        [MenuItem("GnoMore/Construir lobby")]
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
            Debug.Log($"{LogPrefix} Lobby built in {PrefabPath}.");
        }

        private static void BuildContents(GameObject root)
        {
            var ui = root.GetComponent<StartMatchUI>();
            if (ui == null)
            {
                Debug.LogError($"{LogPrefix} StartMatchUI not found on the prefab root.");
                return;
            }

            var panel = root.transform.Find("Panel");
            var startButton = new SerializedObject(ui).FindProperty("startButton").objectReferenceValue as Button;
            if (panel == null || startButton == null)
            {
                Debug.LogError($"{LogPrefix} Expected a 'Panel' child and a wired start button.");
                return;
            }

            var font = FindFont(root);
            var lobby = Child(panel, "Lobby", out var lobbyCreated);
            if (lobbyCreated)
                Stretch(lobby);

            Text(lobby, "RoomCodeLabel", "CÓDIGO DE SALA", 28f, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Center, font, out var codeLabelRect, out var c0);
            if (c0) Place(codeLabelRect, Center, Center, Center, new Vector2(0f, 440f), new Vector2(700f, 40f));

            var codeText = Text(lobby, "RoomCodeText", "—", 84f, Color.white, TextAlignmentOptions.Center, font, out var codeRect, out var c1);
            if (c1) Place(codeRect, Center, Center, Center, new Vector2(0f, 370f), new Vector2(900f, 100f));

            var copy = BuildButton(lobby, "CopyButton", "Copiar código", 28f, font, new Vector2(0f, 290f), new Vector2(280f, 60f), out var copyLabel);

            var header = Text(lobby, "PlayersHeader", "Jugadores", 38f, Color.white, TextAlignmentOptions.Center, font, out var headerRect, out var c2);
            if (c2) Place(headerRect, Center, Center, Center, new Vector2(0f, 200f), new Vector2(900f, 50f));

            var rowsRect = Child(lobby, "PlayerRows", out var rowsCreated);
            if (rowsCreated)
                Place(rowsRect, Center, Center, TopCenter, new Vector2(0f, 160f), new Vector2(900f, RowCount * RowHeight));

            var rows = new TMP_Text[RowCount];
            for (var i = 0; i < RowCount; i++)
            {
                rows[i] = Text(rowsRect, $"Row{i}", "Jugador", 32f, Color.white, TextAlignmentOptions.Center, font, out var rowRect, out var rowCreated);
                rows[i].richText = true;
                if (rowCreated)
                {
                    Place(rowRect, TopCenter, TopCenter, TopCenter, new Vector2(0f, -i * RowHeight), new Vector2(900f, RowHeight));
                    rows[i].gameObject.SetActive(false);
                }
            }

            var camperInfo = Text(lobby, "CamperInfo", "", 34f, Color.white, TextAlignmentOptions.Center, font, out var camperRect, out var c3);
            camperInfo.richText = true;
            if (c3) Place(camperRect, Center, Center, Center, new Vector2(0f, -330f), new Vector2(1200f, 50f));

            var status = Text(lobby, "StatusText", "", 30f, new Color(1f, 0.75f, 0.35f), TextAlignmentOptions.Center, font, out var statusRect, out var c4);
            if (c4) Place(statusRect, Center, Center, Center, new Vector2(0f, -390f), new Vector2(1200f, 40f));

            var ready = BuildButton(lobby, "ReadyButton", "Estoy listo [R]", 30f, font, new Vector2(-180f, -470f), new Vector2(320f, 80f), out var readyLabel);

            // The existing start button moves next to the ready button (once, when the lobby is first built).
            var startRect = (RectTransform)startButton.transform;
            if (lobbyCreated)
            {
                startRect.SetParent(lobby, false);
                Place(startRect, Center, Center, Center, new Vector2(180f, -470f), new Vector2(320f, 80f));
            }
            else if (startRect.parent != lobby)
            {
                startRect.SetParent(lobby, false);
            }

            var startLabel = startButton.GetComponentInChildren<TMP_Text>(true);
            if (startLabel != null)
            {
                startLabel.text = "Empezar";
                startLabel.fontSize = 30f;
            }

            var so = new SerializedObject(ui);
            Wire(so, "startLabel", startLabel);
            Wire(so, "roomCodeText", codeText);
            Wire(so, "copyButton", copy);
            Wire(so, "copyLabel", copyLabel);
            Wire(so, "playersHeader", header);
            Wire(so, "camperInfo", camperInfo);
            Wire(so, "statusText", status);
            Wire(so, "readyButton", ready);
            Wire(so, "readyLabel", readyLabel);

            var rowsProperty = so.FindProperty("playerRows");
            rowsProperty.arraySize = rows.Length;
            for (var i = 0; i < rows.Length; i++)
                rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_FontAsset FindFont(GameObject root)
        {
            var existing = root.GetComponentInChildren<TMP_Text>(true);
            return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
        }

        private static Button BuildButton(RectTransform parent, string name, string label, float size, TMP_FontAsset font,
            Vector2 position, Vector2 dimensions, out TMP_Text labelText)
        {
            var rect = Child(parent, name, out var created);
            var image = GetOrAdd<Image>(rect.gameObject);
            var button = GetOrAdd<Button>(rect.gameObject);
            if (created)
            {
                Place(rect, Center, Center, Center, position, dimensions);
                image.color = Color.white;
                button.targetGraphic = image;
            }

            labelText = Text(rect, "Label", label, size, Color.black, TextAlignmentOptions.Center, font, out var labelRect, out var labelCreated);
            if (labelCreated)
                Stretch(labelRect);

            return button;
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

        private static void Wire(SerializedObject target, string field, Object value)
        {
            var property = target.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"{LogPrefix} StartMatchUI has no serialized field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
        }

        #endregion
    }
}
