// Editor-only tool (C2.5). Menu GnoMore/Construir feel de petrificación: idempotently builds the Petrified Volume profile,
// the "PetrifyVolume", "PetrifyDust" and "PetrifyFragments" children on the Gnome prefab, adds PetrifyFeel and wires it.
// The Volume lives on the prefab (not the scene) because only the owner drives its weight; remote copies stay at 0.
using BetterTogether.Audio;
using BetterTogether.Gaze;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BetterTogether.EditorTools
{
    public static class PetrifyFeelBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string ProfilePath = "Assets/_Project/Settings/Volumes/Petrified.asset";
        private const string MaterialPath = "Assets/_Project/Materials/StoneDust.mat";
        private const string TexturePath = "Assets/_Project/Art/FX/star.png";
        private const string CuePath = "Assets/_Project/Audio/SFX/SFX_Petrify.asset"; // Provisional crack.
        private const string VolumeName = "PetrifyVolume";
        private const string DustName = "PetrifyDust";
        private const string FragmentsName = "PetrifyFragments";
        private const string LogPrefix = "[PetrifyFeelBuilder]";

        [MenuItem("GnoMore/Construir feel de petrificación")]
        public static void Build()
        {
            var profile = EnsureProfile();
            var material = EnsureMaterial();
            if (profile == null || material == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (root.GetComponent<Petrifiable>() == null)
                {
                    Debug.LogError($"{LogPrefix} No Petrifiable on {PrefabPath}.");
                    return;
                }

                var feel = root.GetComponent<PetrifyFeel>();
                if (feel == null)
                    feel = root.AddComponent<PetrifyFeel>();

                var volume = BuildVolume(root.transform, profile);
                var dust = BuildDust(root.transform, material);
                var fragments = BuildFragments(root.transform, material);

                var serialized = new SerializedObject(feel);
                serialized.FindProperty("_volume").objectReferenceValue = volume;
                serialized.FindProperty("_dust").objectReferenceValue = dust;
                serialized.FindProperty("_fragments").objectReferenceValue = fragments;
                serialized.FindProperty("_crackSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(CuePath);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"{LogPrefix} Petrify feel built on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings/Volumes"))
                    AssetDatabase.CreateFolder("Assets/_Project/Settings", "Volumes");

                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            if (!profile.TryGet(out ColorAdjustments adjustments))
                adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.saturation.Override(-80f);

            if (!profile.TryGet(out Vignette vignette))
                vignette = profile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.45f, 0.45f, 0.48f));
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.6f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                {
                    Debug.LogError($"{LogPrefix} Shader 'Universal Render Pipeline/Particles/Unlit' not found.");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            // Configured on every run so the tool stays idempotent (also fixes a material made by an older run).
            material.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.65f));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f); // Alpha
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            BaseShaderGUI.SetupMaterialBlendMode(material);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static GameObject ReplaceChild(Transform parent, string name, Vector3 localPosition)
        {
            var existing = parent.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var child = new GameObject(name); // Default layer, so the camera's Volume layer mask includes it.
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child;
        }

        private static Volume BuildVolume(Transform parent, VolumeProfile profile)
        {
            var child = ReplaceChild(parent, VolumeName, Vector3.zero);
            var volume = child.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        private static ParticleSystem BuildDust(Transform parent, Material material)
        {
            var ps = NewBurst(parent, DustName, new Vector3(0f, 0.3f, 0f), material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new Color(0.65f, 0.65f, 0.68f, 0.8f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            FadeOut(ps);
            return ps;
        }

        private static ParticleSystem BuildFragments(Transform parent, Material material)
        {
            var ps = NewBurst(parent, FragmentsName, new Vector3(0f, 0.3f, 0f), material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new Color(0.5f, 0.5f, 0.53f, 1f);
            main.gravityModifier = 1.5f;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            FadeOut(ps);
            return ps;
        }

        private static ParticleSystem NewBurst(Transform parent, string name, Vector3 localPosition, Material material)
        {
            var child = ReplaceChild(parent, name, localPosition);
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial = material;
            return ps;
        }

        private static void FadeOut(ParticleSystem ps)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }
    }
}
