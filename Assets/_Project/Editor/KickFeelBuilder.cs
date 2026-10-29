// Editor-only tool (C2.4). Menu GnoMore/Construir feel de patada: idempotently builds the "StunStars" particle child on the
// Gnome prefab (stars orbiting over the head), saves its material and assigns Kickable._stunStars. Safe to run many times.
using BetterTogether.Combat;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class KickFeelBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Gnome.prefab";
        private const string MaterialPath = "Assets/_Project/Materials/StunStars.mat";
        private const string ChildName = "StunStars";
        private const string LogPrefix = "[KickFeelBuilder]";

        [MenuItem("GnoMore/Construir feel de patada")]
        public static void Build()
        {
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
                renderer.sharedMaterial = GetMaterial();

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

        private static Material GetMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            material = new Material(shader);
            material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f));
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
