// Editor-only tool (C2.3). Menu GnoMore/Construir feel de salchicha: builds (idempotently) a "Feel" child with sparks, warning
// smoke and a black burnt puff under the Items/Sausage prefab, and wires the Sausage feel fields (particles, clips, cues).
// Re-running it replaces the "Feel" child and refills the fields, so it is safe to run any number of times.
using BetterTogether.Audio;
using BetterTogether.Fireplace;
using UnityEditor;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class SausageFeelBuilder
    {
        private const string MenuPath = "GnoMore/Construir feel de salchicha";
        private const string PrefabPath = "Assets/_Project/Prefabs/Items/Sausage.prefab";
        private const string FeelName = "Feel";
        private const string LogPrefix = "[SausageFeelBuilder]";
        private const string MaterialFolder = "Assets/_Project/Prefabs/Items";

        // Provisional audio: existing assets reused until the audio team delivers dedicated ones.
        private const string SizzleClip = "Assets/_Project/Audio/SFX/Clips/fogata/fuego_loop.wav";
        private const string ReadyCue = "Assets/_Project/Audio/SFX/SFX_FireReady.asset";
        private const string AlarmCue = "Assets/_Project/Audio/SFX/SFX_Watched.asset";
        private const string BurntCue = "Assets/_Project/Audio/SFX/SFX_Petrify.asset";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var sausage = root.GetComponent<Sausage>();
                if (sausage == null)
                {
                    Debug.LogError($"{LogPrefix} {PrefabPath} has no Sausage component.");
                    return;
                }

                var old = root.transform.Find(FeelName);
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);

                var feel = new GameObject(FeelName).transform;
                feel.SetParent(root.transform, false);

                var smokeMat = GetMaterial("Mat_SausageSmoke", new Color(1f, 1f, 1f, 1f));
                var sparkMat = GetMaterial("Mat_SausageSpark", new Color(1f, 0.7f, 0.25f, 1f));

                var sparks = MakeSparks(feel, sparkMat);
                var warn = MakeSmoke(feel, "WarnSmoke", smokeMat, new Color(0.75f, 0.75f, 0.75f, 0.45f), 0.8f, 6f, false);
                var puff = MakeSmoke(feel, "BurntPuff", smokeMat, new Color(0.05f, 0.05f, 0.05f, 0.85f), 1.2f, 0f, true);

                var so = new SerializedObject(sausage);
                Set(so, "_sparks", sparks);
                Set(so, "_warnSmoke", warn);
                Set(so, "_burntPuff", puff);
                Set(so, "_sizzleLoop", AssetDatabase.LoadAssetAtPath<AudioClip>(SizzleClip));
                Set(so, "_readySfx", AssetDatabase.LoadAssetAtPath<SoundCue>(ReadyCue));
                Set(so, "_alarmSfx", AssetDatabase.LoadAssetAtPath<SoundCue>(AlarmCue));
                Set(so, "_burntSfx", AssetDatabase.LoadAssetAtPath<SoundCue>(BurntCue));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"{LogPrefix} Feel built on {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"{LogPrefix} Field {field} not found on Sausage.");
                return;
            }

            property.objectReferenceValue = value;
            if (value == null)
                Debug.LogWarning($"{LogPrefix} {field} left empty (asset missing).");
        }

        private static ParticleSystem MakeSparks(Transform parent, Material material)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.025f);
            main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
            main.gravityModifier = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;

            var emission = ps.emission;
            emission.rateOverTime = 8f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.03f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = FadeOut();

            Finish(go, ps, material);
            return ps;
        }

        private static ParticleSystem MakeSmoke(Transform parent, string name, Material material, Color color, float size,
            float rate, bool burst)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = !burst;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(burst ? 1.2f : 1.5f, burst ? 1.8f : 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(burst ? 0.5f : 0.25f, burst ? 1.1f : 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.15f, size * 0.3f);
            main.startColor = color;
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = burst ? 0f : rate;
            if (burst)
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.04f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = FadeOut();

            Finish(go, ps, material);
            return ps;
        }

        private static ParticleSystem.MinMaxGradient FadeOut()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void Finish(GameObject go, ParticleSystem ps, Material material)
        {
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Soft transparent particle material for URP; created once and reused.
        private static Material GetMaterial(string name, Color tint)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning($"{LogPrefix} URP particle shader not found; using the default particle material.");
                return AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
            }

            material = new Material(shader) { name = name, renderQueue = 3000 };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
