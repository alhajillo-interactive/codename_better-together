// Editor-only (C9.5): bakes the minimap picture of El Abrazo and creates the MinimapArea of the scene. Menu
// GnoMore/Hornear minimapa de El Abrazo. Build runs the same two steps at its end, so a rebuilt level always has a fresh map.
// The picture is an orthographic top-down render of the open scene WITHOUT the crown and its branches (so the trunks, the ramp
// and the roots read), tinted with the paper colour of the UiTheme and darkened at the edges. All public entry points are dialog-free.
using System.IO;
using BetterTogether.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static partial class AbrazoLevelBuilder
    {
        private const string MinimapPath = SpritesDir + "UI/Minimap_Abrazo.png";
        private const string MinimapThemePath = "Assets/_Project/Resources/UiTheme.asset";
        private const string MinimapAreaName = "Minimapa";
        private const string MinimapCanopyPath = "Arbol/Copa";
        private const int MinimapPixels = 512;
        // Half of the world side the picture covers (the picture is square): the area is 2 x this, centred on the world origin.
        private const float MinimapHalfSize = 42f;
        private const float MinimapCameraHeight = 70f;
        private const float MinimapPaperMix = 0.6f;
        private const float MinimapEdgeStart = 0.78f;
        private const float MinimapEdgeDarken = 0.55f;

        [MenuItem("GnoMore/Hornear minimapa de El Abrazo")]
        public static void BakeMinimap()
        {
            if (!OpenAbrazo())
                return;

            _root = FindAbrazoRoot();
            if (_root == null)
            {
                Debug.LogWarning($"{LogPrefix} The scene has no '{RootName}' root; run GnoMore/Construir nivel El Abrazo first.");
                return;
            }

            if (!BakeMinimapPicture())
                return;

            EnsureMinimapArea();
            EditorSceneManager.MarkSceneDirty(_scene);
            EditorSceneManager.SaveScene(_scene);
            AssetDatabase.SaveAssets();
        }

        private static Transform FindAbrazoRoot()
        {
            var roots = _scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == RootName)
                    return roots[i].transform;
            }

            return null;
        }

        // Renders the open scene from above (crown hidden while it renders) and writes Sprites/UI/Minimap_Abrazo.png.
        private static bool BakeMinimapPicture()
        {
            var crown = _root != null ? _root.Find(MinimapCanopyPath) : null;
            var crownWasActive = crown != null && crown.gameObject.activeSelf;
            if (crown != null)
                crown.gameObject.SetActive(false);

            var paper = new Color32(0xF9, 0xEA, 0xC8, 0xFF);
            var ink = new Color32(0x5E, 0x2C, 0x12, 0xFF);
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(MinimapThemePath);
            if (theme != null)
            {
                paper = theme.paper;
                ink = theme.ink;
            }

            var cameraObject = new GameObject("TmpMinimapCamera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(MinimapPixels, MinimapPixels, 24);
            Texture2D texture = null;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = paper;
                camera.farClipPlane = 250f;
                camera.orthographic = true;
                camera.orthographicSize = MinimapHalfSize;
                camera.targetTexture = target;
                camera.transform.SetPositionAndRotation(new Vector3(0f, MinimapCameraHeight, 0f), Quaternion.Euler(90f, 0f, 0f));
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                texture = new Texture2D(MinimapPixels, MinimapPixels, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, MinimapPixels, MinimapPixels), 0, 0);
                RenderTexture.active = previous;
                camera.targetTexture = null;

                TintAsPaper(texture, paper, ink);
                Directory.CreateDirectory(Path.GetDirectoryName(MinimapPath) ?? "Assets");
                File.WriteAllBytes(MinimapPath, texture.EncodeToPNG());
            }
            finally
            {
                if (crown != null)
                    crown.gameObject.SetActive(crownWasActive);

                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
                if (texture != null)
                    Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(MinimapPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureMinimapTexture();
            Debug.Log($"{LogPrefix} Minimap baked to {MinimapPath}: centre (0, 0), size {MinimapHalfSize * 2f} x {MinimapHalfSize * 2f} m.");
            return true;
        }

        // Mixes the picture with the paper colour and darkens the borders towards the ink colour.
        private static void TintAsPaper(Texture2D texture, Color paper, Color ink)
        {
            var pixels = texture.GetPixels32();
            var n = texture.width;
            for (var y = 0; y < n; y++)
            {
                var v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                for (var x = 0; x < n; x++)
                {
                    var u = Mathf.Abs((x + 0.5f) / n * 2f - 1f);
                    var edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MinimapEdgeStart, 1f, Mathf.Max(u, v)));
                    Color c = pixels[y * n + x];
                    c = Color.Lerp(c, c * paper, MinimapPaperMix);
                    c = Color.Lerp(c, ink, edge * MinimapEdgeDarken);
                    c.a = 1f;
                    pixels[y * n + x] = c;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        // Plain 512 texture: no mipmaps, clamped, uncompressed (a small picture that must stay crisp).
        private static void ConfigureMinimapTexture()
        {
            var importer = AssetImporter.GetAtPath(MinimapPath) as TextureImporter;
            if (importer == null)
                return;

            if (importer.textureType == TextureImporterType.Default && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp
                && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.maxTextureSize >= MinimapPixels
                && !importer.alphaIsTransparency)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = MinimapPixels;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        // Creates (or reuses) the MinimapArea under the Abrazo root with the exact centre and size of the bake and the fixed points
        // taken from the constants of this builder.
        private static void EnsureMinimapArea()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapPath);
            var existing = _root.Find(MinimapAreaName);
            var go = existing != null ? existing.gameObject : new GameObject(MinimapAreaName);
            if (existing == null)
                go.transform.SetParent(_root, false);

            if (!go.TryGetComponent<MinimapArea>(out var area))
                area = go.AddComponent<MinimapArea>();

            var serialized = new SerializedObject(area);
            serialized.FindProperty("_map").objectReferenceValue = texture;
            serialized.FindProperty("_worldCenter").vector2Value = Vector2.zero;
            serialized.FindProperty("_worldSize").vector2Value = new Vector2(MinimapHalfSize * 2f, MinimapHalfSize * 2f);

            Vector2[] positions = { FireplaceSpot, CageSpot, PozaCenter };
            UiIcon[] icons = { UiIcon.Fire, UiIcon.Cage, UiIcon.Bucket };
            var points = serialized.FindProperty("_points");
            points.arraySize = positions.Length;
            for (var i = 0; i < positions.Length; i++)
            {
                var element = points.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Position").vector2Value = positions[i];
                element.FindPropertyRelative("Icon").enumValueIndex = (int)icons[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
