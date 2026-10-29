// Editor-only tool (C2.4). Menu GnoMore/Construir feel de patada: idempotently builds the "StunStars" particle child on the
// Gnome prefab (stars orbiting over the head), saves its material and assigns Kickable._stunStars. Safe to run many times.
using System.IO;
using BetterTogether.Combat;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class KickFeelBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string MaterialPath = "Assets/_Project/Materials/StunStars.mat";
        private const string CamperPath = "Assets/_Project/Prefabs/Camper.prefab";
        private const string RingPath = "Assets/_Project/Art/UI/kick_ring.png";
        private const string StarPath = "Assets/_Project/Art/FX/star.png";
        private const string ChildName = "StunStars";
        private const string LogPrefix = "[KickFeelBuilder]";

        [MenuItem("GnoMore/Construir feel de patada")]
        public static void Build()
        {
            var ring = EnsureSprite(RingPath, GenerateRing);
            var star = EnsureSprite(StarPath, GenerateStar);
            if (ring == null || star == null)
                return;

            AssignRing(ring);

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var kickable = root.GetComponent<Kickable>();
                if (kickable == null)
                {
                    Debug.LogError($"{LogPrefix} No Kickable on {PrefabPath}.");
                    return;
                }

                var existing = root.transform.Find(ChildName);
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                var child = new GameObject(ChildName);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.up * 0.6f;

                var ps = child.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 1f;
                main.startLifetime = 0.8f;
                main.startSpeed = 0f;
                main.startSize = 0.09f;
                main.startColor = new Color(1f, 0.85f, 0.2f);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 6;

                var emission = ps.emission;
                emission.rateOverTime = 6f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.16f;
                shape.radiusThickness = 0f;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                var orbit = ps.velocityOverLifetime;
                orbit.enabled = true;
                orbit.space = ParticleSystemSimulationSpace.Local;
                orbit.x = new ParticleSystem.MinMaxCurve(0f);
                orbit.y = new ParticleSystem.MinMaxCurve(0f);
                orbit.z = new ParticleSystem.MinMaxCurve(0f);
                orbit.orbitalY = new ParticleSystem.MinMaxCurve(4f);

                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

                var renderer = child.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var material = GetMaterial(star);
                if (material == null)
                    return;

                renderer.sharedMaterial = material;

                var serialized = new SerializedObject(kickable);
                serialized.FindProperty("_stunStars").objectReferenceValue = ps;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"{LogPrefix} StunStars built on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssignRing(Sprite ring)
        {
            var camper = PrefabUtility.LoadPrefabContents(CamperPath);
            try
            {
                var kick = camper.GetComponent<CamperKick>();
                if (kick == null)
                {
                    Debug.LogError($"{LogPrefix} No CamperKick on {CamperPath}.");
                    return;
                }

                var serialized = new SerializedObject(kick);
                serialized.FindProperty("_ringSprite").objectReferenceValue = ring;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(camper, CamperPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(camper);
            }
        }

        private static Material GetMaterial(Sprite star)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError($"{LogPrefix} Shader 'Universal Render Pipeline/Particles/Unlit' not found.");
                return null;
            }

            material = new Material(shader);
            material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f));
            material.SetTexture("_BaseMap", star.texture);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            AssetDatabase.CreateAsset(material, MaterialPath);
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                Debug.LogError($"{LogPrefix} Could not create {MaterialPath}.");
                return null;
            }

            return material;
        }

        #region Generated textures

        private const int TextureSize = 256;

        // Writes the PNG once (an existing file is kept) and makes sure it imports as an uncompressed sprite.
        private static Sprite EnsureSprite(string assetPath, System.Func<float, float, float> alphaAt)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!File.Exists(fullPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                var pixels = new Color32[TextureSize * TextureSize];
                var center = (TextureSize - 1) * 0.5f;
                for (var y = 0; y < TextureSize; y++)
                {
                    for (var x = 0; x < TextureSize; x++)
                    {
                        var alpha = Mathf.Clamp01(alphaAt((x - center) / center, (y - center) / center));
                        pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }

                texture.SetPixels32(pixels);
                File.WriteAllBytes(fullPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Debug.Log($"{LogPrefix} Generated {assetPath}");
            }

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || !importer.alphaIsTransparency)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
                Debug.LogError($"{LogPrefix} {assetPath} did not import as a Sprite.");
            return sprite;
        }

        // Ring at 85 % of the radius with soft edges.
        private static float GenerateRing(float x, float y)
        {
            var d = Mathf.Sqrt(x * x + y * y);
            return 1f - Mathf.Abs(d - 0.85f) / 0.1f;
        }

        // Five-pointed star with a soft edge.
        private static float GenerateStar(float x, float y)
        {
            var d = Mathf.Sqrt(x * x + y * y);
            var angle = Mathf.Atan2(y, x);
            var spike = 0.5f + 0.5f * Mathf.Cos(5f * (angle - Mathf.PI * 0.5f));
            var radius = Mathf.Lerp(0.35f, 0.95f, spike * spike);
            return (radius - d) / 0.08f;
        }

        #endregion
    }
}
