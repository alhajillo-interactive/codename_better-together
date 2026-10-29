// Editor-only tool for the UI scaling card (C5.4).
//  - GnoMore/Ajustar escalado de UI: every Screen Space canvas of MainMenu, CampBryan, Prefabs/UI/*.prefab and Fireplace.prefab
//    gets a CanvasScaler = Scale With Screen Size, 1920x1080, match 0.5. World Space canvases are not touched. Idempotent:
//    values that already match are not written, so a second run changes nothing. Prefabs are processed before the scenes.
//  - GnoMore/Revisar escalado de UI: read-only report. For 16:9 (1920x1080), 16:10 (1680x1050), 21:9 (2560x1080), 4:3 (1024x768)
//    and 1280x720 it resolves each canvas with the scale factor its CanvasScaler would give at that resolution, rebuilds the
//    layout on a throwaway copy and lists every visible Graphic / TMP whose corners fall outside the canvas rectangle.
//    The result goes to the Console and to Temp/ui-scale-report.txt.
// CampIsma and Deivis scenes are never opened. Scenes that were not open are opened additively and closed again.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterTogether.EditorTools
{
    public static class UiScaleAudit
    {
        private const string LogPrefix = "[UiScaleAudit]";
        private const string ReportPath = "Temp/ui-scale-report.txt";
        private const string UiPrefabFolder = "Assets/_Project/Prefabs/UI";
        private const string FireplacePrefab = "Assets/_Project/Prefabs/Fireplace.prefab";
        private const float MatchWidthOrHeight = 0.5f;
        private const float Tolerance = 1f;

        private static readonly string[] ScenePaths =
        {
            "Assets/_Project/Scenes/MainMenu.unity",
            "Assets/_Project/Scenes/CampBryan.unity",
        };

        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        private static readonly (string Label, int Width, int Height)[] Resolutions =
        {
            ("16:9", 1920, 1080),
            ("16:10", 1680, 1050),
            ("21:9", 2560, 1080),
            ("4:3", 1024, 768),
            ("720p", 1280, 720),
        };

        // ---------------------------------------------------------------- Apply

        [MenuItem("GnoMore/Ajustar escalado de UI")]
        public static void Apply()
        {
            var changed = 0;
            var seen = 0;

            foreach (var path in PrefabPaths())
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                var prefabChanged = false;
                try
                {
                    foreach (var canvas in ScreenSpaceRootCanvases(root))
                    {
                        seen++;
                        if (ApplyScaler(canvas, path))
                        {
                            prefabChanged = true;
                            changed++;
                        }
                    }

                    if (prefabChanged)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            foreach (var scenePath in ScenePaths)
            {
                if (!File.Exists(scenePath))
                {
                    Debug.LogWarning($"{LogPrefix} Scene not found: {scenePath}");
                    continue;
                }

                var scene = OpenScene(scenePath, out var openedHere);
                var sceneChanged = false;
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var canvas in ScreenSpaceRootCanvases(go))
                    {
                        seen++;
                        if (ApplyScaler(canvas, scenePath))
                        {
                            sceneChanged = true;
                            changed++;
                        }
                    }
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Screen Space canvases checked: {seen}, changed: {changed}.");
        }

        // Returns true when something was written.
        private static bool ApplyScaler(Canvas canvas, string source)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            var added = false;
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                added = true;
            }

            var so = new SerializedObject(scaler);
            var modeProp = so.FindProperty("m_UiScaleMode");
            var refProp = so.FindProperty("m_ReferenceResolution");
            var matchModeProp = so.FindProperty("m_ScreenMatchMode");
            var matchProp = so.FindProperty("m_MatchWidthOrHeight");

            var dirty = added;
            if (modeProp.intValue != (int)CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                modeProp.intValue = (int)CanvasScaler.ScaleMode.ScaleWithScreenSize;
                dirty = true;
            }

            if (refProp.vector2Value != ReferenceResolution)
            {
                refProp.vector2Value = ReferenceResolution;
                dirty = true;
            }

            if (matchModeProp.intValue != (int)CanvasScaler.ScreenMatchMode.MatchWidthOrHeight)
            {
                matchModeProp.intValue = (int)CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                dirty = true;
            }

            if (!Mathf.Approximately(matchProp.floatValue, MatchWidthOrHeight))
            {
                matchProp.floatValue = MatchWidthOrHeight;
                dirty = true;
            }

            if (!dirty)
                return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scaler);
            Debug.Log($"{LogPrefix} {source}: '{canvas.name}' -> Scale With Screen Size 1920x1080, match {MatchWidthOrHeight}{(added ? " (scaler added)" : string.Empty)}.");
            return true;
        }

        // ---------------------------------------------------------------- Report

        [MenuItem("GnoMore/Revisar escalado de UI")]
        public static void Report()
        {
            // source :: hierarchy path -> list of "resolution (left 12, bottom 5)".
            var findings = new SortedDictionary<string, List<string>>();
            var canvasLines = new List<string>();
            var canvasCount = 0;

            foreach (var path in PrefabPaths())
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var canvas in ScreenSpaceRootCanvases(root))
                    {
                        canvasCount++;
                        CheckCanvas(canvas, path, findings, canvasLines);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            foreach (var scenePath in ScenePaths)
            {
                if (!File.Exists(scenePath))
                {
                    canvasLines.Add($"Scene not found: {scenePath}");
                    continue;
                }

                var scene = OpenScene(scenePath, out var openedHere);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var canvas in ScreenSpaceRootCanvases(go))
                    {
                        canvasCount++;
                        CheckCanvas(canvas, scenePath, findings, canvasLines);
                    }
                }

                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }

            var sb = new StringBuilder();
            sb.AppendLine("UI SCALE REPORT (C5.4)");
            sb.Append("Resolutions:");
            foreach (var r in Resolutions)
                sb.Append($" {r.Label} {r.Width}x{r.Height};");
            sb.AppendLine();
            sb.AppendLine($"Screen Space root canvases checked: {canvasCount}");
            sb.AppendLine();
            sb.AppendLine("CANVASES (scaler mode, reference, match):");
            foreach (var line in canvasLines)
                sb.AppendLine("  " + line);
            sb.AppendLine();

            if (findings.Count == 0)
            {
                sb.AppendLine("OUTSIDE ELEMENTS: none in any resolution.");
            }
            else
            {
                sb.AppendLine($"OUTSIDE ELEMENTS: {findings.Count} (canvas units, amount outside each side)");
                foreach (var pair in findings)
                    sb.AppendLine($"  {pair.Key}  =>  {string.Join(" | ", pair.Value)}");
            }

            var text = sb.ToString();
            File.WriteAllText(ReportPath, text);
            Debug.Log($"{LogPrefix}\n{text}\n(saved to {ReportPath})");
        }

        private static void CheckCanvas(Canvas canvas, string source, SortedDictionary<string, List<string>> findings, List<string> canvasLines)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            canvasLines.Add(scaler == null
                ? $"{source} :: {canvas.name}: NO CanvasScaler (scale 1)"
                : $"{source} :: {canvas.name}: {scaler.uiScaleMode}, ref {scaler.referenceResolution.x:0}x{scaler.referenceResolution.y:0}, match {scaler.matchWidthOrHeight:0.##}");

            foreach (var res in Resolutions)
            {
                var scale = ScaleFor(scaler, res.Width, res.Height);
                var size = new Vector2(res.Width / scale, res.Height / scale);

                // Throwaway copy as a World Space canvas of exactly the size the scaler would give, so the anchors resolve as at runtime.
                var copy = Object.Instantiate(canvas.gameObject);
                try
                {
                    copy.SetActive(true);
                    var copyCanvas = copy.GetComponent<Canvas>();
                    copyCanvas.renderMode = RenderMode.WorldSpace;
                    var scalerCopy = copy.GetComponent<CanvasScaler>();
                    if (scalerCopy != null)
                        Object.DestroyImmediate(scalerCopy);

                    var rt = (RectTransform)copy.transform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.localScale = Vector3.one;
                    rt.localRotation = Quaternion.identity;
                    rt.position = Vector3.zero;
                    rt.sizeDelta = size;
                    rt.anchoredPosition = Vector2.zero;

                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    Canvas.ForceUpdateCanvases();

                    CollectOutside(rt, canvas.transform, source, res.Label, res.Width, res.Height, findings);
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }
            }
        }

        // Same formula as CanvasScaler.
        private static float ScaleFor(CanvasScaler scaler, int width, int height)
        {
            if (scaler == null)
                return 1f;

            switch (scaler.uiScaleMode)
            {
                case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                    var logWidth = Mathf.Log(width / scaler.referenceResolution.x, 2f);
                    var logHeight = Mathf.Log(height / scaler.referenceResolution.y, 2f);
                    switch (scaler.screenMatchMode)
                    {
                        case CanvasScaler.ScreenMatchMode.Expand:
                            return Mathf.Min(width / scaler.referenceResolution.x, height / scaler.referenceResolution.y);
                        case CanvasScaler.ScreenMatchMode.Shrink:
                            return Mathf.Max(width / scaler.referenceResolution.x, height / scaler.referenceResolution.y);
                        default:
                            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
                    }
                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    return Mathf.Max(0.01f, scaler.scaleFactor);
                default:
                    return 1f;
            }
        }

        private static void CollectOutside(RectTransform copyRoot, Transform originalRoot, string source, string resolutionLabel,
            int width, int height, SortedDictionary<string, List<string>> findings)
        {
            var corners = new Vector3[4];
            var rootRect = copyRoot.rect;
            var graphics = copyRoot.GetComponentsInChildren<Graphic>(true);
            foreach (var graphic in graphics)
            {
                if (!IsVisible(graphic, copyRoot))
                    continue;

                graphic.rectTransform.GetWorldCorners(corners);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var local = copyRoot.InverseTransformPoint(corners[i]);
                    minX = Mathf.Min(minX, local.x);
                    maxX = Mathf.Max(maxX, local.x);
                    minY = Mathf.Min(minY, local.y);
                    maxY = Mathf.Max(maxY, local.y);
                }

                var left = rootRect.xMin - minX;
                var right = maxX - rootRect.xMax;
                var bottom = rootRect.yMin - minY;
                var top = maxY - rootRect.yMax;
                if (left <= Tolerance && right <= Tolerance && bottom <= Tolerance && top <= Tolerance)
                    continue;

                var sb = new StringBuilder();
                sb.Append($"{resolutionLabel} {width}x{height} (");
                var first = true;
                Append(sb, "left", left, ref first);
                Append(sb, "right", right, ref first);
                Append(sb, "top", top, ref first);
                Append(sb, "bottom", bottom, ref first);
                sb.Append(')');

                var key = $"{source} :: {PathFrom(graphic.transform, copyRoot, originalRoot.name)}{(graphic.gameObject.activeInHierarchy ? string.Empty : " [inactive]")}";
                if (!findings.TryGetValue(key, out var list))
                    findings[key] = list = new List<string>();
                list.Add(sb.ToString());
            }
        }

        private static void Append(StringBuilder sb, string side, float amount, ref bool first)
        {
            if (amount <= Tolerance)
                return;
            if (!first)
                sb.Append(", ");
            sb.Append($"{side} {amount:0}");
            first = false;
        }

        // Skips what the player cannot see: disabled or transparent graphics, empty text, hidden groups and anything clipped by a mask.
        private static bool IsVisible(Graphic graphic, RectTransform root)
        {
            if (!graphic.enabled || graphic.color.a <= 0.01f)
                return false;

            var rect = graphic.rectTransform.rect;
            if (rect.width <= 0.01f || rect.height <= 0.01f)
                return false;

            if (graphic is TMPro.TMP_Text text && string.IsNullOrEmpty(text.text))
                return false;

            for (var t = graphic.transform; t != null; t = t.parent)
            {
                if (t != graphic.transform && (t.GetComponent<Mask>() != null || t.GetComponent<RectMask2D>() != null))
                    return false;

                var group = t.GetComponent<CanvasGroup>();
                if (group != null && group.alpha <= 0.01f)
                    return false;

                if (t == root)
                    break;
            }

            return true;
        }

        private static string PathFrom(Transform leaf, Transform copyRoot, string rootName)
        {
            var parts = new List<string>();
            for (var t = leaf; t != null && t != copyRoot; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return parts.Count == 0 ? rootName : rootName + "/" + string.Join("/", parts);
        }

        // ---------------------------------------------------------------- Shared helpers

        private static IEnumerable<string> PrefabPaths()
        {
            var result = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabFolder }))
                result.Add(AssetDatabase.GUIDToAssetPath(guid));
            if (File.Exists(FireplacePrefab))
                result.Add(FireplacePrefab);
            return result;
        }

        // Root canvases (not nested in another canvas) that are not World Space.
        private static IEnumerable<Canvas> ScreenSpaceRootCanvases(GameObject root)
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                var parent = canvas.transform.parent;
                if (parent != null && parent.GetComponentInParent<Canvas>(true) != null)
                    continue;
                if (canvas.renderMode == RenderMode.WorldSpace)
                    continue;
                yield return canvas;
            }
        }

        // Reuses a scene that is already open; otherwise opens it additively so the user's open scenes are untouched.
        private static Scene OpenScene(string path, out bool openedHere)
        {
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded)
            {
                openedHere = false;
                return scene;
            }

            openedHere = true;
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }
    }
}
