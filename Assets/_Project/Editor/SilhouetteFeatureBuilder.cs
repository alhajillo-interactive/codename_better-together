// Editor-only tool (C2.7). Menu GnoMore/Construir siluetas por Renderer Feature: idempotently
//  1. makes sure the "Silhouette" render layer exists (TagManager, written through the API),
//  2. adds two RenderObjects features (stencil mask, then ink outline) to PC_Renderer and Mobile_Renderer,
//  3. moves the visible character renderers (Camper/Gnome prefabs) to that layer and removes the baked/duplicated
//     ink hulls, so no mesh is duplicated any more.
// Why a GameObject layer and not a rendering layer: URP 17 RenderObjects.FilterSettings only exposes LayerMask
// (verified in Library/PackageCache); only renderer child objects change layer, never roots or colliders (C0.6 intact).
using System.Collections.Generic;
using BetterTogether.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BetterTogether.EditorTools
{
    public static class SilhouetteFeatureBuilder
    {
        private const string LayerName = "Silhouette";
        private const string MaskFeatureName = "Watercolor Silhouette Mask";
        private const string InkFeatureName = "Watercolor Silhouette Ink";
        private const string MaskMaterialPath = "Assets/_Project/Materials/AcuarelaShader/WatercolorSilhouetteMask.mat";
        private const string InkMaterialPath = "Assets/_Project/Materials/AcuarelaShader/WatercolorOutline.mat";
        private const string LogPrefix = "[SilhouetteFeatureBuilder]";

        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        private static readonly string[] PrefabPaths =
        {
            "Assets/_Project/Prefabs/Camper.prefab",
            "Assets/_Project/Prefabs/Gnome.prefab",
        };

        [MenuItem("GnoMore/Construir siluetas por Renderer Feature")]
        public static void Build()
        {
            var mask = AssetDatabase.LoadAssetAtPath<Material>(MaskMaterialPath);
            var ink = AssetDatabase.LoadAssetAtPath<Material>(InkMaterialPath);
            if (mask == null || ink == null)
            {
                Debug.LogError($"{LogPrefix} Missing watercolor materials.");
                return;
            }

            int layer = EnsureLayer();
            if (layer < 0)
                return;

            foreach (var path in RendererPaths)
                EnsureFeatures(path, layer, mask, ink);

            foreach (var path in PrefabPaths)
                ConvertPrefab(path, layer, mask, ink);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} Done (layer '{LayerName}' = {layer}).");
        }

        private static int EnsureLayer()
        {
            int existing = LayerMask.NameToLayer(LayerName);
            if (existing >= 0)
                return existing;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError($"{LogPrefix} Cannot open TagManager.");
                return -1;
            }

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue))
                    continue;

                slot.stringValue = LayerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"{LogPrefix} Created layer '{LayerName}' at index {i}.");
                return i;
            }

            Debug.LogError($"{LogPrefix} No free user layer for '{LayerName}'.");
            return -1;
        }

        private static void EnsureFeatures(string path, int layer, Material mask, Material ink)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null)
            {
                Debug.LogError($"{LogPrefix} Renderer asset not found: {path}");
                return;
            }

            var maskFeature = FindFeature(data, MaskFeatureName) ?? AddFeature(data, MaskFeatureName);
            var inkFeature = FindFeature(data, InkFeatureName) ?? AddFeature(data, InkFeatureName);
            if (maskFeature == null || inkFeature == null)
                return;

            Configure(maskFeature, layer, mask);
            Configure(inkFeature, layer, ink);
            EnsureOrder(data, maskFeature, inkFeature);

            EditorUtility.SetDirty(maskFeature);
            EditorUtility.SetDirty(inkFeature);
            EditorUtility.SetDirty(data);
            data.SetDirty();
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"{LogPrefix} {path}: mask + ink features ready.");
        }

        private static RenderObjects FindFeature(ScriptableRendererData data, string featureName)
        {
            foreach (var feature in data.rendererFeatures)
                if (feature is RenderObjects renderObjects && feature.name == featureName)
                    return renderObjects;
            return null;
        }

        // Mirrors ScriptableRendererDataEditor.AddComponent (feature as sub-asset + GUID map entry).
        private static RenderObjects AddFeature(ScriptableRendererData data, string featureName)
        {
            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = featureName;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            return feature;
        }

        private static void Configure(RenderObjects feature, int layer, Material material)
        {
            var s = feature.settings;
            s.passTag = feature.name;
            s.Event = RenderPassEvent.AfterRenderingOpaques;
            s.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            s.filterSettings.LayerMask = 1 << layer;
            s.filterSettings.PassNames = new string[0];
            s.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            s.overrideMaterial = material;
            s.overrideMaterialPassIndex = 0;
            s.overrideDepthState = false;       // shader passes already carry ZTest/ZWrite/Stencil
            s.stencilSettings.overrideStencilState = false;
            s.cameraSettings.overrideCamera = false;
        }

        // The mask must be queued before the ink, so the ink's "stencil NotEqual 1" test sees it.
        private static void EnsureOrder(ScriptableRendererData data, RenderObjects first, RenderObjects second)
        {
            var list = data.rendererFeatures;
            int a = list.IndexOf(first);
            int b = list.IndexOf(second);
            if (a < b)
                return;

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.MoveArrayElement(b, a);
            map.MoveArrayElement(b, a);
            so.ApplyModifiedProperties();
        }

        private static void ConvertPrefab(string path, int layer, Material mask, Material ink)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // The old runtime duplicator is replaced by the features; remove it so nothing is duplicated.
                foreach (var legacy in root.GetComponentsInChildren<WatercolorSilhouette>(true))
                    Object.DestroyImmediate(legacy);

                int moved = 0, hullsRemoved = 0, skipped = 0, fixedSlots = 0;
                var renderers = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
                foreach (var renderer in renderers)
                {
                    if (renderer == null || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
                        continue;

                    var materials = renderer.sharedMaterials;
                    Material replacement = null;
                    bool hasInk = false;
                    foreach (var m in materials)
                    {
                        if (m == ink || m == mask) hasInk = true;
                        else if (m != null && replacement == null) replacement = m;
                    }

                    var go = renderer.gameObject;
                    if (replacement == null)
                    {
                        // Baked ink hull: the feature draws the ink now.
                        if (hasInk && RemoveHull(renderer))
                            hullsRemoved++;
                        continue;
                    }

                    if (hasInk)
                    {
                        for (int i = 0; i < materials.Length; i++)
                            if (materials[i] == ink || materials[i] == mask)
                                materials[i] = replacement;
                        renderer.sharedMaterials = materials;
                        fixedSlots++;
                    }

                    if (go.layer == layer)
                        continue;
                    // Never touch roots, physics layers or objects that own a collider (C0.6).
                    if (go == root || go.layer != 0 || go.GetComponent<Collider>() != null)
                    {
                        skipped++;
                        Debug.LogWarning($"{LogPrefix} {path}: '{go.name}' left on layer {go.layer} (root/physics/collider).", go);
                        continue;
                    }

                    go.layer = layer;
                    moved++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"{LogPrefix} {path}: {moved} renderer(s) on '{LayerName}', {hullsRemoved} hull(s) removed, {fixedSlots} ink slot(s) replaced, {skipped} skipped.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Destroys a leaf hull object when it is an added override (safe); otherwise only disables the renderer.
        private static bool RemoveHull(Renderer hull)
        {
            var go = hull.gameObject;
            bool leaf = go.transform.childCount == 0 && go.GetComponents<Component>().Length <= 3;
            if (leaf && PrefabUtility.IsAddedGameObjectOverride(go))
            {
                Object.DestroyImmediate(go);
                return true;
            }

            if (!hull.enabled)
                return false;
            hull.enabled = false;
            return true;
        }
    }
}
